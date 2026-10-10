/*
 * Small Pot content editor.
 *
 * Three things are worth knowing before reading this.
 *
 * 1. The server owns the content. This page never decides whether a file is valid; it sends a draft to
 *    POST /api/save and shows whatever the server says. What it does decide is the *preview*, which is
 *    computed locally from the fields as they are typed so a designer sees the effect before saving.
 *
 * 2. There are two sources of truth for a file's text: the parsed document, which the forms edit, and
 *    the raw JSON, which the "raw JSON" view edits. Whichever was touched last is what gets saved.
 *
 * 3. Nothing here holds game copy. Every Persian string and every number comes from the data files, so
 *    the page cannot disagree with the game about what a key says.
 */

'use strict';

(() => {

    // -----------------------------------------------------------------------
    // Palette
    //
    // Mirrors SceneKit.Palette in the game. The colours are art direction rather than content, which is
    // why they live in code on both sides instead of in config.json.
    // -----------------------------------------------------------------------
    const PALETTE = {
        pot: '#C86A40',
        potShade: '#A9532E',
        soilWet: '#4A3327',
        soilDry: '#A5865F',
        stem: '#5CA143',
        leaf: '#6EBF4F',
        leafThirsty: '#8EA85C',
        blossom: '#F3A3C3',
        blossomHeart: '#F7D64A',
        sky: '#BBE0F2',
        panel: '#FFFCF6',
        ink: '#4A3B2E',
        accent: '#5CA143',
        caution: '#D98B3A'
    };

    const STRING_FILES = ['strings-extra.json', 'strings-base.json', 'strings.json'];
    const MANAGED_FILES = [
        'config.json',
        'pairs.json',
        'strings-base.json',
        'strings.json',
        'strings-extra.json',
        'dev.json'
    ];
    const SUPPLIED_ONLY = ['config.json', 'pairs.json', 'strings-base.json', 'strings.json'];

    // Keys the game formats with values. Only used to explain a preview, not to validate.
    const PLACEHOLDERS = {
        'water.cost': ['n'],
        'game.prompt': ['word'],
        'game.wrong': ['answer'],
        'game.reward': ['n'],
        'report.grew': ['state'],
        'game.round': ['n', 'total'],
        'pot.growth_progress': ['done', 'total'],
        'settings.data_source_line': ['file', 'source'],
        'settings.data_source_count': ['source', 'n'],
        'update.detail': ['n']
    };

    // -----------------------------------------------------------------------
    // Model
    // -----------------------------------------------------------------------
    const app = {
        version: 0,
        pushedAt: null,
        files: [],
        original: {},     // name -> the text as the server last reported it
        docs: {},         // name -> parsed document, edited by the forms
        texts: {},        // name -> raw JSON draft, edited by the raw view
        results: {},      // name -> { valid, errors, warnings } as the server last reported
        exists: {},       // name -> whether the working copy has the file
        formEdited: new Set(),
        rawEdited: new Set(),
        rawOpen: new Set(),
        selected: 'config.json',
        busy: false
    };

    // -----------------------------------------------------------------------
    // Small helpers
    // -----------------------------------------------------------------------
    function el(tag, props, ...children) {
        const node = document.createElement(tag);
        for (const key of Object.keys(props || {})) {
            const value = props[key];
            if (value === undefined || value === null) {
                continue;
            }
            if (key === 'class') {
                node.className = value;
            } else if (key === 'text') {
                node.textContent = value;
            } else if (key === 'html') {
                node.innerHTML = value;
            } else if (key === 'value' || key === 'checked' || key === 'disabled') {
                node[key] = value;
            } else if (key.startsWith('on') && typeof value === 'function') {
                node.addEventListener(key.slice(2), value);
            } else {
                node.setAttribute(key, value === true ? '' : String(value));
            }
        }
        for (const child of children.flat(4)) {
            if (child === null || child === undefined || child === false) {
                continue;
            }
            node.append(child.nodeType ? child : document.createTextNode(String(child)));
        }
        return node;
    }

    function clear(node) {
        while (node.firstChild) {
            node.removeChild(node.firstChild);
        }
        return node;
    }

    function isObject(value) {
        return value !== null && typeof value === 'object' && !Array.isArray(value);
    }

    function isText(value) {
        return typeof value === 'string' && value.trim() !== '';
    }

    function serialize(document_) {
        return JSON.stringify(document_, null, 2) + '\n';
    }

    function fill(template, values) {
        if (typeof template !== 'string') {
            return '';
        }
        return template.replace(/\{([a-zA-Z_][a-zA-Z0-9_]*)\}/g,
            (whole, name) => (values[name] === undefined ? whole : String(values[name])));
    }

    function capitalize(text) {
        return text ? text.charAt(0).toUpperCase() + text.slice(1) : text;
    }

    function camelMorningKey(from, to) {
        return from + 'To' + capitalize(to);
    }

    function plural(count, one, many) {
        return count + ' ' + (count === 1 ? one : many);
    }

    function formatNumber(value, digits) {
        if (!Number.isFinite(value)) {
            return '-';
        }
        const rounded = Math.round(value * 100) / 100;
        return String(digits === undefined ? rounded : Number(value.toFixed(digits)));
    }

    function formatWhen(iso) {
        if (!iso) {
            return 'never';
        }
        const when = new Date(iso);
        return Number.isNaN(when.getTime()) ? iso : when.toLocaleString();
    }

    // -----------------------------------------------------------------------
    // Server calls
    // -----------------------------------------------------------------------
    async function api(method, url, body) {
        const options = { method, headers: {} };
        if (body !== undefined) {
            options.headers['Content-Type'] = 'application/json';
            options.body = JSON.stringify(body);
        }
        const response = await fetch(url, options);
        let payload = null;
        try {
            payload = await response.json();
        } catch (error) {
            payload = null;
        }
        return { status: response.status, ok: response.ok, payload };
    }

    function banner(message, kind) {
        const node = document.getElementById('banner');
        node.textContent = message || '';
        node.className = 'banner' + (kind ? ' ' + kind : '');
        node.hidden = !message;
    }

    // -----------------------------------------------------------------------
    // State in and out
    // -----------------------------------------------------------------------
    function applyState(state, keepEdited) {
        // Drafts survive a refresh of the state, so saving one file does not throw away unsaved work in
        // another.
        const formDrafts = {};
        for (const name of app.formEdited) {
            formDrafts[name] = app.docs[name];
        }
        const rawDrafts = {};
        for (const name of app.rawEdited) {
            rawDrafts[name] = app.texts[name];
        }

        app.version = state.version;
        app.pushedAt = state.pushedAt;
        app.files = state.files;

        app.original = {};
        app.docs = {};
        app.texts = {};
        app.results = {};
        app.exists = {};
        app.formEdited = new Set();
        app.rawEdited = new Set();

        for (const file of state.files) {
            app.original[file.name] = file.text;
            app.exists[file.name] = file.exists;
            const parsed = typeof file.text === 'string' ? tryParse(file.text) : { ok: false, value: null };
            app.docs[file.name] = parsed.ok ? parsed.value : null;
            app.texts[file.name] = file.text;
            app.results[file.name] = {
                valid: file.valid,
                errors: file.errors || [],
                warnings: file.warnings || []
            };
            if (!parsed.ok && file.exists) {
                // Content that arrived broken (an external editor, say) can only be fixed as text.
                app.rawOpen.add(file.name);
            }
        }

        for (const name of keepEdited || []) {
            if (formDrafts[name] !== undefined) {
                app.docs[name] = formDrafts[name];
                app.formEdited.add(name);
            }
            if (rawDrafts[name] !== undefined) {
                app.texts[name] = rawDrafts[name];
                app.rawEdited.add(name);
            }
        }
    }

    function tryParse(text) {
        try {
            return { ok: true, value: JSON.parse(text), error: null };
        } catch (error) {
            return { ok: false, value: null, error: error.message };
        }
    }

    /** The text that would be saved for a file right now. */
    function currentText(name) {
        if (app.rawEdited.has(name)) {
            return app.texts[name];
        }
        if (app.formEdited.has(name) && app.docs[name] !== null) {
            return serialize(app.docs[name]);
        }
        return app.original[name];
    }

    function isEdited(name) {
        return app.formEdited.has(name) || app.rawEdited.has(name);
    }

    function editedNames() {
        return MANAGED_FILES.filter(isEdited);
    }

    function markEdited(name) {
        if (!app.formEdited.has(name)) {
            app.formEdited.add(name);
        }
        renderRail();
        renderTopBar();
        renderPreview();
        updatePushButton();
    }

    function mergedStrings() {
        const merged = {};
        for (const name of STRING_FILES) {
            const document_ = app.docs[name];
            if (isObject(document_) && isObject(document_.strings)) {
                Object.assign(merged, document_.strings);
            }
        }
        return merged;
    }

    function config() {
        return isObject(app.docs['config.json']) ? app.docs['config.json'] : {};
    }

    function pairs() {
        return Array.isArray(app.docs['pairs.json']) ? app.docs['pairs.json'] : [];
    }

    // -----------------------------------------------------------------------
    // Preview maths, mirroring the game
    // -----------------------------------------------------------------------

    /** Mirrors GameConfig.MorningsFor: camel case, snake case, then the default. */
    function resolveMornings(from, to, growthNode) {
        const growth = isObject(growthNode)
            ? growthNode
            : (isObject(config().growth) ? config().growth : {});
        const table = isObject(growth.mornings) ? growth.mornings : {};
        for (const key of [camelMorningKey(from, to), from + '_to_' + to, from + 'To' + to]) {
            const value = Number(table[key]);
            if (Number.isFinite(value)) {
                return { value: Math.max(1, value), explicit: true, key };
            }
        }
        const fallback = Number(growth.defaultMorningsPerTransition);
        return {
            value: Number.isFinite(fallback) ? Math.max(1, fallback) : 1,
            explicit: false,
            key: null
        };
    }

    function stageIds(configNode) {
        const node = isObject(configNode) ? configNode : config();
        const stages = node.growth && node.growth.stages;
        return Array.isArray(stages) ? stages.filter(isText) : [];
    }

    function economy(configNode) {
        const node = isObject(configNode) ? configNode : config();
        const mini = isObject(node.miniGame) ? node.miniGame : {};
        const watering = isObject(node.watering) ? node.watering : {};
        const rounds = Number(mini.rounds);
        const perCorrect = Number(mini.dropsPerCorrect);
        const bonus = Number(mini.bonusDropsIfAllCorrect);
        const threshold = Number(mini.minCorrectForReward);
        const cost = Number(watering.costDrops);
        const best = Number.isFinite(rounds) && Number.isFinite(perCorrect) && Number.isFinite(bonus)
            ? rounds * perCorrect + bonus
            : NaN;
        return {
            rounds,
            perCorrect,
            bonus,
            threshold,
            cost,
            best,
            startDrops: Number(node.startDrops),
            guaranteed: Number.isFinite(threshold) && Number.isFinite(perCorrect)
                ? threshold * perCorrect
                : NaN,
            sessionsPerWatering: Number.isFinite(best) && best > 0 && Number.isFinite(cost)
                ? cost / best
                : NaN
        };
    }

    function growthTotals(configNode) {
        const node = isObject(configNode) ? configNode : config();
        const stages = stageIds(node);
        const steps = [];
        let total = 0;
        for (let index = 0; index + 1 < stages.length; index += 1) {
            const resolved = resolveMornings(stages[index], stages[index + 1], node.growth);
            steps.push({ from: stages[index], to: stages[index + 1], resolved });
            total += resolved.value;
        }
        return { stages, steps, total };
    }

    // -----------------------------------------------------------------------
    // Silhouettes
    //
    // Mirrors Assets/Scripts/Core/PlantVisuals.cs: for a stage id the game knows, the height, leaf
    // pairs, leaf tilt, whether there is a blossom and the soil dryness are fixed; an unknown id is
    // resolved parametrically from its position in the stage list. Thirst adds 26 degrees of leaf
    // droop, shortens the plant by 8% and dries the soil out; watering soaks it.
    // -----------------------------------------------------------------------
    const UNIT = 86;        // pixels per world unit of plant height
    const SOIL_Y = 132;     // where the soil surface sits in the viewBox
    const VIEW_W = 132;
    const VIEW_H = 178;

    function lerp(a, b, t) {
        return a + (b - a) * t;
    }

    function hexToRgb(hex) {
        return [
            parseInt(hex.slice(1, 3), 16),
            parseInt(hex.slice(3, 5), 16),
            parseInt(hex.slice(5, 7), 16)
        ];
    }

    function mixColor(from, to, t) {
        const a = hexToRgb(from);
        const b = hexToRgb(to);
        const clamped = Math.max(0, Math.min(1, Number.isFinite(t) ? t : 0));
        const channels = a.map((value, index) => Math.round(lerp(value, b[index], clamped)));
        return '#' + channels.map(value => value.toString(16).padStart(2, '0')).join('');
    }

    function stageVisual(id, index, count) {
        switch (id) {
            case 'seed':
                return { height: 0.10, leafPairs: 0, tilt: 0, flower: false, soil: 0.10 };
            case 'sprout':
                return { height: 0.35, leafPairs: 1, tilt: 6, flower: false, soil: 0.12 };
            case 'plant':
                return { height: 0.70, leafPairs: 2, tilt: 0, flower: false, soil: 0.14 };
            case 'bloom':
                return { height: 1.00, leafPairs: 2, tilt: -4, flower: true, soil: 0.14 };
            default: {
                const t = count <= 1 ? 1 : index / (count - 1);
                return {
                    height: lerp(0.16, 1.05, t),
                    leafPairs: Math.round(lerp(0, 3, t)),
                    tilt: 0,
                    flower: index >= count - 1,
                    soil: 0.14
                };
            }
        }
    }

    function potVisual(id, index, count, flags) {
        const stage = stageVisual(id, index, count);
        let height = stage.height;
        let tilt = stage.tilt;
        let soil = stage.soil;
        if (flags.thirsty) {
            tilt += 26;
            height *= 0.92;
            soil = 0.90;
        }
        if (flags.watered) {
            soil = 0.05;
        }
        return {
            height,
            leafPairs: stage.leafPairs,
            tilt,
            flower: stage.flower,
            soil,
            leaf: flags.thirsty ? PALETTE.leafThirsty : PALETTE.leaf
        };
    }

    function ellipse(cx, cy, rx, ry, rotation, fill) {
        return `<ellipse cx="${cx.toFixed(1)}" cy="${cy.toFixed(1)}" rx="${rx.toFixed(1)}" ` +
            `ry="${ry.toFixed(1)}" transform="rotate(${rotation.toFixed(1)} ${cx.toFixed(1)} ${cy.toFixed(1)})" ` +
            `fill="${fill}"/>`;
    }

    function droplet(cx, cy, fill, struck) {
        const path = `M ${cx} ${cy - 9} C ${cx + 7} ${cy - 1} ${cx + 6} ${cy + 7} ${cx} ${cy + 7} ` +
            `C ${cx - 6} ${cy + 7} ${cx - 7} ${cy - 1} ${cx} ${cy - 9} Z`;
        return `<path d="${path}" fill="${fill}"/>` + (struck
            ? `<line x1="${cx - 8}" y1="${cy + 8}" x2="${cx + 8}" y2="${cy - 11}" ` +
              `stroke="#C0503A" stroke-width="2.5" stroke-linecap="round"/>`
            : '');
    }

    function silhouetteMarkup(id, index, count, flags) {
        const visual = potVisual(id, index, count, flags);
        const soilColor = mixColor(PALETTE.soilWet, PALETTE.soilDry, visual.soil);
        const stemTop = SOIL_Y - visual.height * UNIT;
        const parts = [];

        parts.push(`<rect x="0" y="0" width="${VIEW_W}" height="${VIEW_H}" rx="10" fill="${PALETTE.sky}"/>`);
        // Window sill: the pot always stands on something in the game, so it does here too.
        parts.push(`<rect x="0" y="${SOIL_Y + 34}" width="${VIEW_W}" height="14" fill="#E6D8C3"/>`);

        // Pot: a trapezoid, a rim, a shadowed right half and a saucer.
        parts.push(`<path d="M 32 ${SOIL_Y} L 100 ${SOIL_Y} L 92 ${SOIL_Y + 36} L 40 ${SOIL_Y + 36} Z" fill="${PALETTE.pot}"/>`);
        parts.push(`<path d="M 66 ${SOIL_Y} L 100 ${SOIL_Y} L 92 ${SOIL_Y + 36} L 66 ${SOIL_Y + 36} Z" fill="${PALETTE.potShade}" opacity="0.55"/>`);
        parts.push(`<rect x="28" y="${SOIL_Y - 7}" width="76" height="11" rx="3" fill="${PALETTE.potShade}"/>`);
        parts.push(`<rect x="34" y="${SOIL_Y + 34}" width="64" height="8" rx="3" fill="${PALETTE.potShade}"/>`);
        parts.push(ellipse(66, SOIL_Y - 2, 34, 7, 0, soilColor));

        if (visual.leafPairs === 0) {
            // No leaves yet: the game shows the seed shell sitting in the soil.
            parts.push(ellipse(66, SOIL_Y - 8, 12, 8, 0, PALETTE.soilWet));
        } else {
            parts.push(`<line x1="66" y1="${SOIL_Y - 4}" x2="66" y2="${stemTop.toFixed(1)}" stroke="${PALETTE.stem}" stroke-width="4" stroke-linecap="round"/>`);

            for (let pair = 0; pair < visual.leafPairs; pair += 1) {
                const fraction = visual.leafPairs <= 1
                    ? 0.62
                    : 0.34 + (0.52 * pair) / (visual.leafPairs - 1);
                const y = SOIL_Y - 4 - visual.height * UNIT * fraction;
                const angle = 24 + pair * 38;
                const rx = 0.20 * UNIT;
                const ry = 0.055 * UNIT;
                // The right leaf dips clockwise, the left one dips the other way round, so a positive
                // tilt droops both - which is what the game's thirst tilt does.
                for (const rotation of [angle + visual.tilt, 180 - angle - visual.tilt]) {
                    const radians = (rotation * Math.PI) / 180;
                    const cx = 66 + Math.cos(radians) * rx;
                    const cy = y + Math.sin(radians) * rx;
                    parts.push(ellipse(cx, cy, rx, ry, rotation, visual.leaf));
                }
            }
        }

        if (visual.flower) {
            const radius = 0.10 * UNIT;
            for (let petal = 0; petal < 5; petal += 1) {
                const angle = petal * 72;
                const radians = (angle * Math.PI) / 180;
                const cx = 66 + Math.cos(radians) * radius;
                const cy = stemTop + Math.sin(radians) * radius;
                parts.push(ellipse(cx, cy, 0.065 * UNIT, 0.105 * UNIT, angle + 90, PALETTE.blossom));
            }
            parts.push(`<circle cx="66" cy="${stemTop.toFixed(1)}" r="${(0.045 * UNIT).toFixed(1)}" fill="${PALETTE.blossomHeart}"/>`);
        }

        if (flags.thirsty) {
            parts.push(droplet(112, 20, PALETTE.caution, true));
        } else if (flags.watered) {
            parts.push(droplet(112, 20, PALETTE.accent, false));
        }

        return `<svg viewBox="0 0 ${VIEW_W} ${VIEW_H}" role="img">${parts.join('')}</svg>`;
    }

    function silhouetteCard(id, index, count, flags, caption, labelKey) {
        const merged = mergedStrings();
        const label = typeof merged[labelKey] === 'string' ? merged[labelKey] : null;
        const card = el('div', { class: 'sil-card' });
        card.append(el('div', { html: silhouetteMarkup(id, index, count, flags) }));
        if (caption) {
            card.append(el('div', { class: 'sil-id', text: caption }));
        }
        card.append(el('div', {
            class: 'sil-label',
            dir: 'rtl',
            text: label === null ? labelKey : label
        }));
        if (label === null) {
            card.append(el('div', { class: 'sil-missing', text: 'no string for ' + labelKey }));
        }
        return card;
    }

    // -----------------------------------------------------------------------
    // Rendering: top bar, rail
    // -----------------------------------------------------------------------
    function renderTopBar() {
        document.getElementById('version').textContent = String(app.version);
        document.getElementById('pushed-at').textContent = formatWhen(app.pushedAt);

        const edited = editedNames();
        const chip = document.getElementById('edit-state');
        const dirty = app.files.filter(file => file.dirty).length;
        chip.className = 'chip ' + (edited.length > 0 ? 'dirty' : 'clean');
        chip.textContent = edited.length > 0
            ? plural(edited.length, 'unsaved file', 'unsaved files')
            : dirty > 0
                ? plural(dirty, 'edited file', 'edited files') + ' ready to push'
                : 'everything matches the supplied copies';
    }

    function dotClass(name) {
        const result = app.results[name];
        if (!result || !result.valid) {
            return 'bad';
        }
        return result.warnings.length > 0 ? 'warn' : 'ok';
    }

    function renderRail() {
        const rail = clear(document.getElementById('rail'));

        for (const name of MANAGED_FILES) {
            const result = app.results[name] || { valid: true, errors: [], warnings: [] };
            const file = app.files.find(entry => entry.name === name);
            const badges = el('div', { class: 'badges' });
            if (file && file.dirty) {
                badges.append(el('span', { class: 'badge dirty', text: 'edited' }));
            }
            if (isEdited(name)) {
                badges.append(el('span', { class: 'badge draft', text: 'unsaved' }));
            }
            if (!app.exists[name]) {
                badges.append(el('span', { class: 'badge', text: 'absent' }));
            }

            const counts = [];
            if (result.errors.length > 0) {
                counts.push(result.errors.length + ' error' + (result.errors.length === 1 ? '' : 's'));
            }
            if (result.warnings.length > 0) {
                counts.push(result.warnings.length + ' warning' + (result.warnings.length === 1 ? '' : 's'));
            }

            rail.append(el('button', {
                type: 'button',
                class: 'rail-item' + (name === app.selected ? ' selected' : ''),
                onclick: () => selectFile(name)
            },
                el('span', { class: 'dot ' + dotClass(name) }),
                el('span', { class: 'name', text: name }),
                el('span', { class: 'counts', text: counts.join(', ') })
            ));
            if (badges.childNodes.length > 0) {
                rail.lastChild.append(badges);
            }
        }

        document.getElementById('rail-note').textContent =
            'config.json and pairs.json drive the game; the strings files are merged ' +
            'extra \u2192 base \u2192 strings, so strings.json wins.';
    }

    function selectFile(name) {
        app.selected = name;
        renderRail();
        renderEditor();
        renderPreview();
    }

    // -----------------------------------------------------------------------
    // Field helpers
    // -----------------------------------------------------------------------
    function numberField(label, hint, object, key, options) {
        const settings = options || {};
        const input = el('input', {
            type: 'number',
            value: object[key] === undefined || object[key] === null ? '' : object[key],
            min: settings.min,
            max: settings.max,
            step: settings.step || (settings.integer ? 1 : 'any'),
            oninput: event => {
                const raw = event.target.value;
                if (raw === '') {
                    delete object[key];
                } else {
                    object[key] = Number(raw);
                }
                markEdited(app.selected);
            }
        });
        return el('div', { class: 'field' },
            el('label', { text: label }),
            input,
            el('span', { class: 'hint', text: hint || '' })
        );
    }

    function textField(label, hint, object, key, options) {
        const settings = options || {};
        const input = el('input', {
            type: 'text',
            dir: settings.rtl ? 'rtl' : undefined,
            class: settings.rtl ? 'rtl' : undefined,
            value: object[key] === undefined || object[key] === null ? '' : object[key],
            list: settings.list,
            oninput: event => {
                object[key] = event.target.value;
                markEdited(app.selected);
            }
        });
        return el('div', { class: 'field' },
            el('label', { text: label }),
            input,
            el('span', { class: 'hint', text: hint || '' })
        );
    }

    function checkField(label, hint, object, key) {
        return el('div', { class: 'field' },
            el('label', { text: label }),
            el('input', {
                type: 'checkbox',
                checked: object[key] === true,
                onchange: event => {
                    object[key] = event.target.checked;
                    markEdited(app.selected);
                }
            }),
            el('span', { class: 'hint', text: hint || '' })
        );
    }

    function group(title, hint, ...children) {
        return el('div', { class: 'group' },
            el('h3', { text: title }),
            hint ? el('p', { class: 'hint', text: hint }) : null,
            children
        );
    }

    // -----------------------------------------------------------------------
    // Editor: config.json
    // -----------------------------------------------------------------------
    function ensureConfig() {
        const document_ = app.docs['config.json'];
        if (!isObject(document_)) {
            return null;
        }
        document_.miniGame = isObject(document_.miniGame) ? document_.miniGame : {};
        document_.watering = isObject(document_.watering) ? document_.watering : {};
        document_.thirsty = isObject(document_.thirsty) ? document_.thirsty : {};
        document_.ui = isObject(document_.ui) ? document_.ui : {};
        document_.growth = isObject(document_.growth) ? document_.growth : {};
        if (!Array.isArray(document_.growth.stages)) {
            document_.growth.stages = [];
        }
        if (!isObject(document_.growth.mornings)) {
            document_.growth.mornings = {};
        }
        return document_;
    }

    /** Rewrites growth.mornings so it holds exactly the consecutive pairs the game reads. */
    function rebuildMornings(document_) {
        const stages = document_.growth.stages.filter(isText);
        const previous = isObject(document_.growth.mornings) ? document_.growth.mornings : {};
        const next = {};
        for (let index = 0; index + 1 < stages.length; index += 1) {
            const from = stages[index];
            const to = stages[index + 1];
            const resolved = resolveMornings(from, to);
            next[camelMorningKey(from, to)] = resolved.value;
            // Keep a value that was written in another spelling, so renaming a stage's capitalisation
            // does not silently drop its mornings.
            if (resolved.key && resolved.key !== camelMorningKey(from, to)) {
                delete previous[resolved.key];
            }
        }
        document_.growth.mornings = next;
    }

    function renderMorningsInto(container, document_) {
        clear(container);
        const stages = document_.growth.stages.filter(isText);
        if (stages.length < 2) {
            container.append(el('p', { class: 'note', text: 'Add a second stage to get a transition.' }));
            return;
        }
        const table = el('table', { class: 'grid' },
            el('thead', {}, el('tr', {},
                el('th', { text: 'From' }),
                el('th', { text: 'To' }),
                el('th', { text: 'Watered mornings' }),
                el('th', { text: 'Written as' })
            ))
        );
        const body = el('tbody');
        for (let index = 0; index + 1 < stages.length; index += 1) {
            const from = stages[index];
            const to = stages[index + 1];
            const key = camelMorningKey(from, to);
            const value = document_.growth.mornings[key];
            body.append(el('tr', {},
                el('td', { class: 'stage', text: from }),
                el('td', { class: 'stage', text: to }),
                el('td', { class: 'narrow' }, el('input', {
                    type: 'number',
                    min: 1,
                    step: 1,
                    value: value === undefined ? 1 : value,
                    oninput: event => {
                        const raw = Number(event.target.value);
                        document_.growth.mornings[key] = Number.isFinite(raw) ? Math.max(1, raw) : 1;
                        markEdited(app.selected);
                    }
                })),
                el('td', { class: 'stage', text: key })
            ));
        }
        table.append(body);
        container.append(table);
    }

    function configEditor() {
        const document_ = ensureConfig();
        if (!document_) {
            return [brokenEditor('config.json')];
        }

        const mini = document_.miniGame;
        const growthHost = el('div');
        const morningsHost = el('div');

        function refreshGrowth() {
            const stages = document_.growth.stages;
            clear(growthHost);
            stages.forEach((stage, index) => {
                growthHost.append(el('div', { class: 'field' },
                    el('label', { text: 'Stage ' + (index + 1) }),
                    el('input', {
                        type: 'text',
                        value: stage,
                        oninput: event => {
                            // Anything is accepted here, including a duplicate or an empty id: the
                            // server reports that as an error and the designer sees why.
                            document_.growth.stages[index] = event.target.value;
                            rebuildMornings(document_);
                            renderMorningsInto(morningsHost, document_);
                            markEdited(app.selected);
                        }
                    }),
                    el('span', { class: 'hint' },
                        el('button', {
                            type: 'button',
                            class: 'icon',
                            title: 'Move up',
                            disabled: index === 0,
                            onclick: () => {
                                const stagesNow = document_.growth.stages;
                                [stagesNow[index - 1], stagesNow[index]] = [stagesNow[index], stagesNow[index - 1]];
                                rebuildMornings(document_);
                                renderEditor();
                            }
                        }, '\u2191'),
                        ' ',
                        el('button', {
                            type: 'button',
                            class: 'icon',
                            title: 'Move down',
                            disabled: index === stages.length - 1,
                            onclick: () => {
                                const stagesNow = document_.growth.stages;
                                [stagesNow[index + 1], stagesNow[index]] = [stagesNow[index], stagesNow[index + 1]];
                                rebuildMornings(document_);
                                renderEditor();
                            }
                        }, '\u2193'),
                        ' ',
                        el('button', {
                            type: 'button',
                            class: 'icon danger',
                            title: 'Remove this stage',
                            disabled: stages.length <= 1,
                            onclick: () => {
                                document_.growth.stages.splice(index, 1);
                                rebuildMornings(document_);
                                renderEditor();
                            }
                        }, '\u00d7')
                    )
                ));
            });

            growthHost.append(el('div', { class: 'field' },
                el('label', { text: 'Add a stage' }),
                el('button', {
                    type: 'button',
                    onclick: () => {
                        const stagesNow = document_.growth.stages;
                        let counter = stagesNow.length + 1;
                        while (stagesNow.indexOf('stage' + counter) !== -1) {
                            counter += 1;
                        }
                        stagesNow.push('stage' + counter);
                        rebuildMornings(document_);
                        renderEditor();
                    }
                }, 'Add stage'),
                el('span', { class: 'hint', text: 'The game walks the list in order; the last stage is the goal.' })
            ));

            renderMorningsInto(morningsHost, document_);
        }

        refreshGrowth();

        return [
            group('Drops', 'The currency. The counter at the top of the game shows it.',
                numberField('Start drops', 'What the player has before playing anything.', document_, 'startDrops', { integer: true, min: 0 })
            ),
            group('Minigame', 'Rounds of antonyms against one shared clock.',
                numberField('Rounds', 'Words asked per session.', mini, 'rounds', { integer: true, min: 1 }),
                numberField('Choices shown', 'The antonym plus its distractors.', mini, 'choicesShown', { integer: true, min: 2 }),
                numberField('Seconds', 'The single timer for the whole session.', mini, 'totalSeconds', { min: 1 }),
                numberField('Reward threshold', 'Correct answers needed before the reward moment pays out.', mini, 'minCorrectForReward', { integer: true, min: 0 }),
                numberField('Drops per correct answer', 'Paid for every correct round.', mini, 'dropsPerCorrect', { integer: true, min: 0 }),
                numberField('All-correct bonus', 'Added on top when no round is missed.', mini, 'bonusDropsIfAllCorrect', { integer: true, min: 0 })
            ),
            group('Watering', 'The water button spends drops and prepares the pot for the next morning.',
                numberField('Cost in drops', 'What one watering costs.', document_.watering, 'costDrops', { integer: true, min: 0 })
            ),
            group('Thirst', 'A morning with no water makes the plant thirsty. Thirst never takes growth back; it only blocks the next one.',
                numberField('Dry mornings before thirst', 'How many mornings without water it takes.', document_.thirsty, 'afterMorningsWithoutWater', { integer: true, min: 1 })
            ),
            group('Animation', 'Motion timings. Reduce motion in the game zeroes these but keeps every end state.',
                numberField('Max animation (ms)', 'The budget feedback timings are derived from.', document_.ui, 'maxAnimationMs', { min: 1 }),
                numberField('Counter roll (ms)', 'How long the drop counter takes to roll to a new number.', document_.ui, 'numberCountUpMs', { min: 0 })
            ),
            group('Growth', 'The stage list, and how many watered mornings each step takes. Growth always happens on the next morning, never on the spot.',
                growthHost,
                el('h4', { text: 'Mornings per transition' }),
                morningsHost,
                el('div', { class: 'field' },
                    el('label', { text: 'Default mornings' }),
                    el('input', {
                        type: 'number',
                        min: 1,
                        step: 1,
                        value: document_.growth.defaultMorningsPerTransition === undefined
                            ? ''
                            : document_.growth.defaultMorningsPerTransition,
                        oninput: event => {
                            const raw = event.target.value;
                            if (raw === '') {
                                delete document_.growth.defaultMorningsPerTransition;
                            } else {
                                document_.growth.defaultMorningsPerTransition = Number(raw);
                            }
                            renderMorningsInto(morningsHost, document_);
                            markEdited(app.selected);
                        }
                    }),
                    el('span', { class: 'hint', text: 'Used when a transition has no entry above. Leave empty for 1.' })
                )
            )
        ];
    }

    // -----------------------------------------------------------------------
    // Editor: strings files
    // -----------------------------------------------------------------------
    /** The highest-priority file *above* `name` that carries the key, i.e. what overrides it here. */
    function higherPrioritySource(key, name) {
        const from = STRING_FILES.indexOf(name);
        for (let index = STRING_FILES.length - 1; index > from; index -= 1) {
            const source = app.docs[STRING_FILES[index]];
            if (isObject(source) && isObject(source.strings) &&
                typeof source.strings[key] === 'string') {
                return { name: STRING_FILES[index], text: source.strings[key] };
            }
        }
        return null;
    }

    /** The file that actually decides a key's text, whichever file is being edited. */
    function effectiveSource(key) {
        for (let index = STRING_FILES.length - 1; index >= 0; index -= 1) {
            const source = app.docs[STRING_FILES[index]];
            if (isObject(source) && isObject(source.strings) &&
                typeof source.strings[key] === 'string') {
                return { name: STRING_FILES[index], text: source.strings[key] };
            }
        }
        return null;
    }

    function stringsEditor(name) {
        const document_ = app.docs[name];
        if (!isObject(document_) || !isObject(document_.strings)) {
            return [brokenEditor(name)];
        }

        const keys = Object.keys(document_.strings);
        const table = el('table', { class: 'grid' },
            el('thead', {}, el('tr', {},
                el('th', { text: 'Key' }),
                el('th', { text: 'Text' }),
                el('th', { text: 'Priority' }),
                el('th', {})
            ))
        );
        const body = el('tbody');

        for (const key of keys) {
            const overridden = higherPrioritySource(key, name);
            const row = el('tr', {},
                el('td', { class: 'stage', text: key }),
                el('td', {}, el('input', {
                    type: 'text',
                    dir: 'rtl',
                    class: 'rtl',
                    value: document_.strings[key],
                    oninput: event => {
                        document_.strings[key] = event.target.value;
                        markEdited(app.selected);
                    }
                })),
                el('td', { class: 'narrow' },
                    overridden
                        ? el('span', {
                            class: 'badge',
                            title: overridden.name + ' wins with: ' + overridden.text,
                            text: 'overridden by ' + overridden.name
                        })
                        : el('span', { class: 'badge supplied', text: 'effective' })
                ),
                el('td', { class: 'narrow' }, el('button', {
                    type: 'button',
                    class: 'icon danger',
                    title: 'Remove this key from ' + name,
                    onclick: () => {
                        delete document_.strings[key];
                        renderEditor();
                    }
                }, '\u00d7'))
            );
            body.append(row);
        }
        table.append(body);

        // Keys this file does not carry: shown so the designer sees the whole effective set, and so an
        // override is a deliberate act rather than an accident.
        const inherited = Object.keys(mergedStrings()).filter(key => !(key in document_.strings));
        const inheritedTable = el('table', { class: 'grid' },
            el('thead', {}, el('tr', {},
                el('th', { text: 'Key' }),
                el('th', { text: 'Effective text' }),
                el('th', { text: 'From' }),
                el('th', {})
            ))
        );
        const inheritedBody = el('tbody');
        for (const key of inherited) {
            const source = effectiveSource(key);
            inheritedBody.append(el('tr', { class: 'muted' },
                el('td', { class: 'stage', text: key }),
                el('td', { class: 'rtl', dir: 'rtl', text: source ? source.text : '' }),
                el('td', { class: 'stage', text: source ? source.name : '' }),
                el('td', { class: 'narrow' }, el('button', {
                    type: 'button',
                    class: 'icon',
                    title: 'Copy it into ' + name + ' so this file decides',
                    onclick: () => {
                        document_.strings[key] = source ? source.text : '';
                        markEdited(app.selected);
                        renderEditor();
                    }
                }, 'copy in'))
            ));
        }
        inheritedTable.append(inheritedBody);

        const allKeys = Array.from(new Set(MANAGED_FILES
            .filter(other => STRING_FILES.indexOf(other) !== -1)
            .reduce((list, other) => list.concat(
                isObject(app.docs[other]) && isObject(app.docs[other].strings)
                    ? Object.keys(app.docs[other].strings)
                    : []
            ), []))).sort();

        const listId = 'keys-for-' + name.replace(/\W/g, '');
        const newKey = el('input', { type: 'text', list: listId, placeholder: 'new.key.name' });

        return [
            group('Strings', 'Merged lowest priority first: strings-extra.json, then strings-base.json, then strings.json. The supplied file wins.',
                table,
                el('div', { class: 'field' },
                    el('label', { text: 'Add a key' }),
                    newKey,
                    el('span', { class: 'hint' },
                        el('button', {
                            type: 'button',
                            onclick: () => {
                                const key = newKey.value.trim();
                                if (key === '') {
                                    return;
                                }
                                const source = effectiveSource(key);
                                document_.strings[key] = source ? source.text : '';
                                markEdited(app.selected);
                                renderEditor();
                            }
                        }, 'Add key'),
                        ' Copies the effective text when the key already exists elsewhere.'
                    )
                ),
                el('datalist', { id: listId }, allKeys.map(key => el('option', { value: key })))
            ),
            group('Inherited keys', 'Not in this file, so another file decides. Copy one in to override it here.',
                el('details', { class: 'inherited', open: inherited.length > 0 ? '' : undefined },
                    el('summary', { text: plural(inherited.length, 'key', 'keys') + ' inherited' }),
                    inherited.length > 0 ? inheritedTable : el('p', { class: 'note', text: 'Nothing is inherited.' })
                )
            )
        ];
    }

    // -----------------------------------------------------------------------
    // Editor: pairs.json
    // -----------------------------------------------------------------------
    function pairsEditor() {
        const document_ = app.docs['pairs.json'];
        if (!Array.isArray(document_)) {
            return [brokenEditor('pairs.json')];
        }

        const table = el('table', { class: 'grid' },
            el('thead', {}, el('tr', {},
                el('th', { text: '#' }),
                el('th', { text: 'Word' }),
                el('th', { text: 'Antonym' }),
                el('th', { text: 'Wrong 1' }),
                el('th', { text: 'Wrong 2' }),
                el('th', {})
            ))
        );
        const body = el('tbody');

        document_.forEach((entry, index) => {
            if (!isObject(entry)) {
                return;
            }
            if (!Array.isArray(entry.wrong)) {
                entry.wrong = [];
            }
            while (entry.wrong.length < 2) {
                entry.wrong.push('');
            }
            const cell = (key, target, position) => el('td', {}, el('input', {
                type: 'text',
                dir: 'rtl',
                class: 'rtl',
                value: position === undefined ? entry[key] : entry[key][position],
                oninput: event => {
                    if (position === undefined) {
                        entry[key] = event.target.value;
                    } else {
                        entry[key][position] = event.target.value;
                    }
                    markEdited(app.selected);
                }
            }));

            body.append(el('tr', {},
                el('td', { class: 'stage', text: String(index + 1) }),
                cell('word'),
                cell('opposite'),
                cell('wrong', null, 0),
                cell('wrong', null, 1),
                el('td', { class: 'narrow' }, el('button', {
                    type: 'button',
                    class: 'icon danger',
                    title: 'Remove this pair',
                    onclick: () => {
                        document_.splice(index, 1);
                        renderEditor();
                    }
                }, '\u00d7'))
            ));
        });
        table.append(body);

        return [
            group('Pairs', 'One word per round; the player picks its antonym. The round order and the option order are shuffled in the game.',
                table,
                el('div', { class: 'field' },
                    el('label', { text: 'Add a pair' }),
                    el('button', {
                        type: 'button',
                        onclick: () => {
                            document_.push({ word: '', opposite: '', wrong: ['', ''] });
                            renderEditor();
                        }
                    }, 'Add row'),
                    el('span', { class: 'hint', text: 'At least 3 usable entries are needed for 5 rounds of 3 choices.' })
                )
            )
        ];
    }

    // -----------------------------------------------------------------------
    // Editor: dev.json
    // -----------------------------------------------------------------------
    function devEditor() {
        const document_ = isObject(app.docs['dev.json']) ? app.docs['dev.json'] : {};
        if (!isObject(app.docs['dev.json'])) {
            return [brokenEditor('dev.json')];
        }
        return [
            group('Development', 'This file only exists in development builds. The server URL is where the app looks for fresh content.',
                textField('Server URL', 'For example http://192.168.1.20:8787, or http://127.0.0.1:8787 over adb reverse.', document_, 'dataServerUrl'),
                checkField('Show data source', 'Shows which file each piece of content came from.', document_, 'showDataSourceInfo'),
                checkField('Show debug panel', 'Adds the next-morning button and the save reset.', document_, 'showDebugPanel')
            )
        ];
    }

    // -----------------------------------------------------------------------
    // Editor: shell
    // -----------------------------------------------------------------------
    function brokenEditor(name) {
        return group('Cannot show a form',
            'The file does not parse, so it can only be edited as raw JSON. Fix the syntax and the form ' +
            'comes back.',
            el('div', { class: 'field' },
                el('label', { text: 'Open raw JSON' }),
                el('button', { type: 'button', onclick: () => openRaw(name) }, 'Edit as text'),
                el('span', { class: 'hint', text: '' })
            )
        );
    }

    function openRaw(name) {
        app.texts[name] = currentText(name);
        app.rawOpen.add(name);
        renderEditor();
    }

    function closeRaw(name) {
        const parsed = tryParse(app.texts[name]);
        if (!parsed.ok) {
            banner('That is not valid JSON yet: ' + parsed.error, 'bad');
            return;
        }
        app.docs[name] = parsed.value;
        app.rawEdited.delete(name);
        app.rawOpen.delete(name);
        // Untouched raw text is not an edit, so opening the view and closing it leaves the file clean.
        if (app.texts[name] !== app.original[name] && parsed.value !== null) {
            app.formEdited.add(name);
        }
        renderAll();
    }

    function rawEditor(name) {
        const textarea = el('textarea', {
            spellcheck: 'false',
            value: app.texts[name] === null ? '' : app.texts[name],
            oninput: event => {
                app.texts[name] = event.target.value;
                app.rawEdited.add(name);
                const parsed = tryParse(event.target.value);
                parseNote.textContent = parsed.ok ? 'Parses as JSON.' : 'Does not parse: ' + parsed.error;
                parseNote.className = 'note' + (parsed.ok ? '' : ' error');
                renderRail();
                renderTopBar();
                renderPreview();
                updatePushButton();
            }
        });
        const parseNote = el('p', { class: 'note', text: 'Parses as JSON.' });
        const parsed = tryParse(app.texts[name]);
        if (!parsed.ok) {
            parseNote.textContent = 'Does not parse: ' + parsed.error;
        }

        return group('Raw JSON', 'The exact bytes that would be saved. Nothing is hidden from you here.',
            textarea,
            parseNote,
            el('div', { class: 'field' },
                el('label', { text: 'Back to the form' }),
                el('button', { type: 'button', onclick: () => closeRaw(name) }, 'Use this text'),
                el('span', { class: 'hint', text: 'The form is rebuilt from what you typed.' })
            )
        );
    }

    function validationPanel(name) {
        const result = app.results[name] || { valid: true, errors: [], warnings: [] };
        const panel = el('div', { class: 'validation' });

        if (isEdited(name)) {
            panel.append(el('p', {
                class: 'note',
                text: 'Unsaved changes. The preview below already uses them; press Save to check them against ' +
                    'the game\u2019s rules and write them to the working copy.'
            }));
        }

        if (result.errors.length > 0) {
            panel.append(el('div', { class: 'validation errors' },
                el('h4', { text: plural(result.errors.length, 'error', 'errors') + ' \u2014 saving is refused' }),
                el('ul', {}, result.errors.map(message => el('li', { text: message })))
            ));
        }
        if (result.warnings.length > 0) {
            panel.append(el('div', { class: 'validation warnings' },
                el('h4', { text: plural(result.warnings.length, 'warning', 'warnings') }),
                el('ul', {}, result.warnings.map(message => el('li', { text: message })))
            ));
        }
        if (result.errors.length === 0 && result.warnings.length === 0 && !isEdited(name)) {
            panel.append(el('p', { class: 'validation ok', text: 'No problems found in this file.' }));
        }
        return panel;
    }

    function renderEditor() {
        const host = document.getElementById('editor');
        const scroll = host.scrollTop;
        clear(host);

        const name = app.selected;
        const file = app.files.find(entry => entry.name === name) || { dirty: false, suppliedIdentical: false };
        const isRaw = app.rawOpen.has(name);

        host.append(el('div', { class: 'file-head' },
            el('h1', { text: name }),
            file.supplied
                ? el('span', { class: 'badge supplied', text: 'supplied by the employer' })
                : el('span', { class: 'badge', text: 'optional' }),
            file.dirty
                ? el('span', { class: 'badge dirty', text: 'differs from the supplied copy' })
                : el('span', { class: 'badge', text: 'identical to the supplied copy' }),
            el('span', { class: 'spacer' },
                el('button', {
                    type: 'button',
                    class: isRaw ? 'primary' : '',
                    title: 'Show the exact text, and edit it directly.',
                    onclick: () => (isRaw ? closeRaw(name) : openRaw(name))
                }, isRaw ? 'Hide raw JSON' : 'Raw JSON'),
                el('button', {
                    type: 'button',
                    disabled: !file.dirty && !isEdited(name),
                    title: 'Restore this file from the supplied copy.',
                    onclick: () => resetFile(name)
                }, 'Reset to supplied'),
                el('button', {
                    type: 'button',
                    class: 'primary',
                    disabled: app.busy,
                    title: 'Validate and write this file to the working copy.',
                    onclick: () => saveFile(name)
                }, 'Save')
            )
        ));

        host.append(el('p', {
            class: 'file-sub',
            text: (app.exists[name] ? '' : 'not present in the working copy  \u00b7  ') +
                'the app fetches /' + name
        }));

        if (isRaw) {
            host.append(rawEditor(name));
        } else if (name === 'config.json') {
            host.append(...configEditor());
        } else if (name === 'pairs.json') {
            host.append(...pairsEditor());
        } else if (name === 'dev.json') {
            host.append(...devEditor());
        } else {
            host.append(...stringsEditor(name));
        }

        host.append(validationPanel(name));
        host.scrollTop = scroll;
    }

    // -----------------------------------------------------------------------
    // Preview
    // -----------------------------------------------------------------------
    function previewGrowth() {
        const { stages, steps, total } = growthTotals();
        const card = el('div', { class: 'pcard' }, el('h3', { text: 'Growth chain' }));

        if (stages.length === 0) {
            card.append(el('p', { class: 'note', text: 'No stages configured.' }));
            return card;
        }

        const list = el('ul', { class: 'chain' });
        stages.forEach((stage, index) => {
            list.append(el('li', {},
                el('span', { class: 'stage', text: stage }),
                index === 0 ? el('span', { class: 'arrow', text: 'start' }) : null
            ));
            if (index < steps.length) {
                const step = steps[index];
                list.append(el('li', {},
                    el('span', { class: 'arrow', text: '\u2193' }),
                    el('span', {
                        class: 'mornings',
                        text: plural(step.resolved.value, 'watered morning', 'watered mornings')
                    }),
                    step.resolved.explicit
                        ? null
                        : el('span', { class: 'fallback', text: 'default' })
                ));
            }
        });
        card.append(list);

        const cost = economy().cost;
        card.append(el('dl', {},
            el('dt', { text: 'Mornings to the last stage' }),
            el('dd', { text: plural(total, 'morning', 'mornings') }),
            el('dt', { text: 'Drops for one full growth' }),
            el('dd', { text: Number.isFinite(cost) ? String(total * cost) : '-' }),
            el('dt', { text: 'Stages' }),
            el('dd', { text: String(stages.length) })
        ));
        return card;
    }

    function previewEconomy() {
        const money = economy();
        const card = el('div', { class: 'pcard' }, el('h3', { text: 'Economy' }));
        const sessions = money.sessionsPerWatering;

        card.append(el('dl', {},
            el('dt', { text: 'Starting drops' }),
            el('dd', { text: formatNumber(money.startDrops) }),
            el('dt', { text: 'Per correct answer' }),
            el('dd', { text: formatNumber(money.perCorrect) }),
            el('dt', { text: 'Reward threshold' }),
            el('dd', { text: formatNumber(money.threshold) + ' correct' }),
            el('dt', { text: 'All-correct bonus' }),
            el('dd', { text: formatNumber(money.bonus) }),
            el('dt', { text: 'Best possible session' }),
            el('dd', { text: formatNumber(money.best) + ' drops' }),
            el('dt', { text: 'Guaranteed at the threshold' }),
            el('dd', { text: formatNumber(money.guaranteed) + ' drops' }),
            el('dt', { text: 'One watering costs' }),
            el('dd', { text: formatNumber(money.cost) + ' drops' }),
            el('dt', { text: 'Sessions per watering' }),
            el('dd', {
                text: Number.isFinite(sessions)
                    ? (Number.isInteger(sessions) ? String(sessions) : sessions.toFixed(2)) +
                      ' of a perfect session'
                    : 'never - nothing can earn drops'
            })
        ));

        if (Number.isFinite(money.cost) && Number.isFinite(money.startDrops) && money.startDrops < money.cost) {
            card.append(el('p', {
                class: 'note',
                text: 'The player cannot afford the first watering, so the minigame has to come first.'
            }));
        }
        return card;
    }

    function previewMinigame() {
        const mini = isObject(config().miniGame) ? config().miniGame : {};
        const money = economy();
        const card = el('div', { class: 'pcard' }, el('h3', { text: 'Minigame' }));
        const seconds = Number(mini.totalSeconds);
        const rounds = Number(mini.rounds);

        card.append(el('dl', {},
            el('dt', { text: 'Rounds' }),
            el('dd', { text: formatNumber(rounds) }),
            el('dt', { text: 'Choices per round' }),
            el('dd', { text: formatNumber(mini.choicesShown) }),
            el('dt', { text: 'Clock' }),
            el('dd', { text: formatNumber(seconds) + ' s, shared by every round' }),
            el('dt', { text: 'Seconds per round' }),
            el('dd', {
                text: Number.isFinite(seconds) && Number.isFinite(rounds) && rounds > 0
                    ? (seconds / rounds).toFixed(1) + ' s'
                    : '-'
            }),
            el('dt', { text: 'A wrong answer' }),
            el('dd', { text: 'Shows the right word, takes nothing away' }),
            el('dt', { text: 'Below the threshold' }),
            el('dd', { text: 'An encouraging message, no blame, nothing subtracted' })
        ));

        if (Number.isFinite(money.threshold) && Number.isFinite(rounds) && money.threshold > rounds) {
            card.append(el('p', { class: 'note', text: 'The threshold is above the number of rounds, so no session can ever pay out.' }));
        }
        return card;
    }

    function previewSilhouettes() {
        const stages = stageIds();
        const card = el('div', { class: 'pcard' }, el('h3', { text: 'Pot states' }));
        const grid = el('div', { class: 'silhouettes' });

        if (stages.length === 0) {
            card.append(el('p', { class: 'note', text: 'No stages configured.' }));
            return card;
        }

        stages.forEach((stage, index) => {
            grid.append(silhouetteCard(stage, index, stages.length, {}, null, 'pot.state.' + stage));
        });

        // Thirst is a state, not a stage, so it is previewed on the second stage - the one the player
        // is most likely to leave dry.
        const thirstyIndex = Math.min(1, stages.length - 1);
        grid.append(silhouetteCard(stages[thirstyIndex], thirstyIndex, stages.length, { thirsty: true },
            'thirsty ' + stages[thirstyIndex], 'pot.state.thirsty'));
        grid.append(silhouetteCard(stages[thirstyIndex], thirstyIndex, stages.length, { watered: true },
            'watered ' + stages[thirstyIndex], 'pot.watered'));

        card.append(grid);
        card.append(el('p', {
            class: 'note',
            text: 'Heights, leaf pairs, tilt, blossom and soil dryness mirror Assets/Scripts/Core/' +
                'PlantVisuals.cs. Thirst adds 26 degrees of droop, shortens the plant and dries the soil; ' +
                'a stage id the game does not know is drawn from its position in the list.'
        }));
        return card;
    }

    function previewRound() {
        const card = el('div', { class: 'pcard' }, el('h3', { text: 'A round, as the player sees it' }));
        const merged = mergedStrings();
        const list = pairs().filter(entry => isObject(entry) && isText(entry.word) &&
            isText(entry.opposite) && Array.isArray(entry.wrong));
        const first = list[0];

        if (!first) {
            card.append(el('p', { class: 'note', text: 'No usable pair yet.' }));
            return card;
        }

        const prompt = fill(merged['game.prompt'] || 'game.prompt', { word: first.word });
        card.append(el('div', { class: 'round-preview' },
            el('div', { class: 'prompt rtl', dir: 'rtl', text: prompt }),
            el('div', { class: 'options', dir: 'rtl' },
                el('span', { class: 'answer', text: first.opposite, title: 'the antonym' }),
                ...first.wrong.filter(isText).map(word => el('span', { class: 'wrong', text: word }))
            ),
            el('p', { class: 'note', text: 'Options are shuffled in the game; the antonym is marked here only for you.' })
        ));

        card.append(el('dl', {},
            el('dt', { text: 'Usable pairs' }),
            el('dd', { text: String(list.length) }),
            el('dt', { text: 'Rounds asked' }),
            el('dd', { text: formatNumber(isObject(config().miniGame) ? config().miniGame.rounds : undefined) })
        ));
        return card;
    }

    function renderPreview() {
        const host = clear(document.getElementById('preview'));
        host.append(previewGrowth(), previewEconomy(), previewMinigame(), previewSilhouettes(), previewRound());
    }

    // -----------------------------------------------------------------------
    // Actions
    // -----------------------------------------------------------------------
    function updatePushButton() {
        const invalid = MANAGED_FILES.filter(name => app.results[name] && !app.results[name].valid);
        const edited = editedNames();
        const button = document.getElementById('push');
        button.disabled = app.busy || invalid.length > 0 || edited.length > 0;
        button.title = invalid.length > 0
            ? 'Cannot push while ' + invalid.join(', ') + ' is invalid.'
            : edited.length > 0
                ? 'Save ' + edited.join(', ') + ' first; a push publishes saved content.'
                : 'Tell the app to reload its content.';
    }

    async function saveFile(name) {
        app.busy = true;
        updatePushButton();
        const text = currentText(name);
        const response = await api('POST', '/api/save', { name, text });
        app.busy = false;

        const payload = response.payload;
        if (!payload) {
            banner('The server did not answer the save.', 'bad');
            updatePushButton();
            return;
        }

        if (payload.ok) {
            banner('Saved ' + name + ' to the working copy. Press Push when you want the app to see it.');
            app.rawEdited.delete(name);
            app.formEdited.delete(name);
            await load(true);
            return;
        }

        // Rejected: the working copy still holds the last good text, and the draft stays on screen so it
        // can be fixed in place.
        app.results[name] = {
            valid: false,
            errors: payload.errors || [],
            warnings: payload.warnings || []
        };
        banner('Not saved: ' + name + ' has ' + plural((payload.errors || []).length, 'error', 'errors') + '.', 'bad');
        renderRail();
        renderEditor();
        renderTopBar();
        updatePushButton();
    }

    async function resetFile(name) {
        app.busy = true;
        updatePushButton();
        const keep = editedNames().filter(other => other !== name);
        const response = await api('POST', '/api/reset', { name });
        app.busy = false;

        if (response.payload && response.payload.files) {
            applyState(response.payload, keep);
            app.rawOpen.delete(name);
            banner('Restored ' + name + ' from the supplied copy.');
            renderAll();
        } else {
            banner('Could not reset ' + name + ': ' + ((response.payload && response.payload.error) || response.status), 'bad');
            updatePushButton();
        }
    }

    async function resetAll() {
        app.busy = true;
        updatePushButton();
        const response = await api('POST', '/api/resetAll');
        app.busy = false;

        if (response.payload && response.payload.files) {
            applyState(response.payload);
            app.rawOpen.clear();
            banner('Every file restored from the supplied copies.');
            renderAll();
        } else {
            banner('Could not reset: ' + ((response.payload && response.payload.error) || response.status), 'bad');
            updatePushButton();
        }
    }

    async function push() {
        // The button is disabled in these cases, but the reason is stated here too so a keyboard or
        // programmatic click cannot publish a draft.
        const invalid = MANAGED_FILES.filter(name => app.results[name] && !app.results[name].valid);
        const unsaved = editedNames();
        if (invalid.length > 0 || unsaved.length > 0) {
            banner('Not pushed: ' + (invalid.length > 0
                ? invalid.join(', ') + ' is invalid.'
                : 'save ' + unsaved.join(', ') + ' first. A push publishes saved content.'), 'warn');
            return;
        }

        app.busy = true;
        updatePushButton();
        const response = await api('POST', '/api/push');
        app.busy = false;
        const payload = response.payload;

        if (response.status === 409) {
            banner('Not pushed: ' + ((payload && payload.error) || 'some files are invalid.'), 'bad');
            updatePushButton();
            return;
        }
        if (!payload || !payload.ok) {
            banner('Push failed: ' + ((payload && payload.error) || response.status), 'bad');
            updatePushButton();
            return;
        }

        banner('Pushed version ' + payload.version + '. The app picks it up on its next poll.');
        await load(false);
    }

    async function load(keepEdits) {
        const response = await api('GET', '/api/state');
        if (!response.payload || !response.payload.files) {
            banner('The server did not answer /api/state.', 'bad');
            return;
        }
        applyState(response.payload, keepEdits ? editedNames() : []);
        renderAll();
    }

    function renderAll() {
        renderTopBar();
        renderRail();
        renderEditor();
        renderPreview();
        updatePushButton();
    }

    // -----------------------------------------------------------------------
    // Boot
    // -----------------------------------------------------------------------
    if (typeof document !== 'undefined') {
        document.getElementById('push').addEventListener('click', push);
        document.getElementById('reset-all').addEventListener('click', resetAll);

        load(false).catch(error => banner('Could not reach the server: ' + error.message, 'bad'));
    }

    /*
     * The preview maths is pure, so the test suite checks it from Node as well as the page using it in
     * the browser. Everything below is only reachable when this file is loaded as a module.
     */
    if (typeof module !== 'undefined' && module.exports) {
        module.exports = {
            stageVisual,
            potVisual,
            silhouetteMarkup,
            resolveMornings,
            growthTotals,
            economy,
            camelMorningKey,
            PALETTE
        };
    }

})();
