#!/usr/bin/env node
/*
 * Small Pot content server.
 *
 * The game keeps all of its state on the device; this server only hands out content. What it adds on
 * top of the plain static host it started as is a *working copy*: a designer edits content in a web
 * page, and only a deliberate push makes the running app pick it up.
 *
 *   node server/server.js [port]        (default 8787)
 *
 * Content model
 *   ../data/          the files the employer supplied. Never written to. The pristine source.
 *   content/          the working copy, seeded from ../data/ on first use. Served and edited.
 *   state.json        the content version and when it was last pushed.
 *
 * Routes the Unity client depends on, exactly:
 *   GET /<file>.json  the working copy, no-store, ETag honoured
 *   GET /version      {"version": n, "pushedAt": iso|null} - what the revision is at this instant
 *   GET /api/watch    held open until the revision changes, so a push is felt in one round trip
 *   GET /health       liveness
 *
 * Routes for the admin page:
 *   GET /                       public/index.html
 *   GET /admin.css, /admin.js   the admin assets
 *   GET /files                  the plain-text listing of what is served
 *   GET /api/state              everything the editor needs, including validation
 *   POST /api/save              validate and write one working-copy file
 *   POST /api/push              bump the version, and answer every client that is watching
 *   POST /api/reset, /api/resetAll   restore from the pristine copy
 *
 * Everything is zero-dependency and synchronous on purpose: this is a single-user tool for a phone on
 * the same desk, and synchronous reads make a save/validate/push sequence trivially correct.
 */

'use strict';

const http = require('http');
const fs = require('fs');
const path = require('path');
const os = require('os');
const crypto = require('crypto');

// ---------------------------------------------------------------------------
// Content model
// ---------------------------------------------------------------------------

const MANAGED_FILES = [
    'config.json',
    'pairs.json',
    'strings-base.json',
    'strings.json',
    'strings-extra.json',
    'dev.json'
];

// The four files the employer supplied. The server never writes to the supplied directory at all, so
// this list only documents which files the byte-for-byte checksum tooling owns.
const SUPPLIED_ONLY_FILES = ['config.json', 'pairs.json', 'strings-base.json', 'strings.json'];

// Lowest priority first: the supplied strings.json wins over strings-base.json, which wins over the
// optional extra file. This is the same merge order the game uses.
const STRING_FILES = ['strings-extra.json', 'strings-base.json', 'strings.json'];

// Missing is fine for these; the game falls back.
const OPTIONAL_FILES = ['strings-extra.json', 'dev.json'];

// Every key the game reads. Checked against the *merged* strings, so an override file is allowed to
// carry only a handful of them.
const REQUIRED_STRING_KEYS = [
    'hud.drops',
    'pot.state.seed',
    'pot.state.sprout',
    'pot.state.plant',
    'pot.state.bloom',
    'pot.state.thirsty',
    'pot.watered',
    'action.water',
    'action.play',
    'action.next_morning',
    'water.cost',
    'water.not_enough',
    'game.title',
    'game.prompt',
    'game.time_left',
    'game.correct',
    'game.wrong',
    'game.reward',
    'game.no_reward',
    'report.title',
    'report.grew',
    'report.thirsty',
    'report.nothing',
    'settings.title',
    'settings.reduce_motion',
    'common.continue',
    'common.close'
];

/*
 * Keys whose call site passes values into the text.
 *
 * A placeholder that the call site fills in but the text lacks disappears silently at runtime (the
 * player just never sees the number), and a placeholder the call site does not fill in is shown to the
 * player as literal braces. Both are worth a warning, and this table is the whole rule: no inference,
 * no guessing from the text itself.
 */
const PLACEHOLDERS = {
    'water.cost': ['n'],
    'game.prompt': ['word'],
    'game.wrong': ['answer'],
    'game.reward': ['n'],
    'report.grew': ['state'],

    // The optional extra file is formatted with values as well, so its keys are listed here too.
    // Without them the shipped extra file would produce false warnings on every load.
    'game.round': ['n', 'total'],
    'pot.growth_progress': ['done', 'total'],
    'settings.data_source_line': ['file', 'source'],
    'settings.data_source_count': ['source', 'n'],
    'update.detail': ['n']
};

const KNOWN_PLACEHOLDERS = Array.from(
    new Set(Object.values(PLACEHOLDERS).reduce((all, list) => all.concat(list), []))
);

const MAX_BODY_BYTES = 2 * 1024 * 1024;

/** How long a watch is held when the client does not say, in seconds. */
const DEFAULT_WATCH_HOLD_SECONDS = 20;

// ---------------------------------------------------------------------------
// Small helpers
// ---------------------------------------------------------------------------

function sha256(buffer) {
    return crypto.createHash('sha256').update(buffer).digest('hex');
}

/**
 * How long a watch may be held, in seconds. The client asks and the server decides, because a watch that
 * never expired would leave a connection the network has already dropped looking like patience.
 */
function clampHoldSeconds(seconds) {
    if (!Number.isFinite(seconds)) {
        return DEFAULT_WATCH_HOLD_SECONDS;
    }
    return Math.min(60, Math.max(2, Math.floor(seconds)));
}

function capitalize(text) {
    return text ? text.charAt(0).toUpperCase() + text.slice(1) : text;
}

/** The key the game looks up first for a transition, e.g. seed + sprout -> "seedToSprout". */
function camelMorningKey(from, to) {
    return from + 'To' + capitalize(to);
}

/** Reads a dotted path, tolerating every missing link. */
function getPath(root, dotted) {
    let node = root;
    for (const part of dotted.split('.')) {
        if (node === null || typeof node !== 'object' || Array.isArray(node)) {
            return undefined;
        }
        node = node[part];
    }
    return node;
}

function isPlainObject(value) {
    return value !== null && typeof value === 'object' && !Array.isArray(value);
}

function isNonEmptyString(value) {
    return typeof value === 'string' && value.trim().length > 0;
}

function parseJson(text) {
    if (typeof text !== 'string') {
        return { ok: false, value: null, error: 'the file is not present' };
    }
    try {
        return { ok: true, value: JSON.parse(text), error: null };
    } catch (error) {
        return { ok: false, value: null, error: error.message };
    }
}

function formatList(items, limit) {
    const shown = items.slice(0, limit === undefined ? 6 : limit);
    const rest = items.length - shown.length;
    return shown.join(', ') + (rest > 0 ? `, and ${rest} more` : '');
}

// ---------------------------------------------------------------------------
// Directories and the working copy
// ---------------------------------------------------------------------------

function pathsFrom(options) {
    const serverDirectory = __dirname;
    return {
        supplied: options.suppliedDirectory || path.resolve(serverDirectory, '..', 'data'),
        working: options.contentDirectory || path.resolve(serverDirectory, 'content'),
        state: options.statePath || path.resolve(serverDirectory, 'state.json'),
        publicDirectory: options.publicDirectory || path.resolve(serverDirectory, 'public')
    };
}

function createStore(paths, log) {
    const state = { version: 1, pushedAt: null };
    let temporaryCounter = 0;

    function ensureDirectory(directory) {
        fs.mkdirSync(directory, { recursive: true });
    }

    /**
     * Writes through a temporary file and renames it into place, so an interrupted save cannot leave a
     * half-written data file for the phone to fetch.
     */
    function writeAtomic(target, data) {
        temporaryCounter += 1;
        const temporary = `${target}.${process.pid}.${temporaryCounter}.tmp`;
        fs.writeFileSync(temporary, data);
        fs.renameSync(temporary, target);
    }

    function readBuffer(directory, name) {
        try {
            return fs.readFileSync(path.join(directory, name));
        } catch (error) {
            return null;
        }
    }

    function loadState() {
        const raw = readBuffer(path.dirname(paths.state), path.basename(paths.state));
        if (raw === null) {
            return;
        }
        const parsed = parseJson(raw.toString('utf8'));
        if (!parsed.ok || !isPlainObject(parsed.value)) {
            log(`state file is unreadable; starting a fresh version counter (${parsed.error})`);
            return;
        }
        const version = Number(parsed.value.version);
        state.version = Number.isFinite(version) ? Math.max(1, Math.floor(version)) : 1;
        state.pushedAt = typeof parsed.value.pushedAt === 'string' ? parsed.value.pushedAt : null;
    }

    function saveState() {
        ensureDirectory(path.dirname(paths.state));
        writeAtomic(paths.state, JSON.stringify(state, null, 2) + '\n');
    }

    function readSupplied(name) {
        return readBuffer(paths.supplied, name);
    }

    function readWorking(name) {
        return readBuffer(paths.working, name);
    }

    /** Copies any managed file that is missing from the working copy. Never overwrites an edit. */
    function seed() {
        ensureDirectory(paths.working);
        for (const name of MANAGED_FILES) {
            if (readWorking(name) !== null) {
                continue;
            }
            const supplied = readSupplied(name);
            if (supplied === null) {
                log(`no supplied copy of ${name}; the working copy starts without it`);
                continue;
            }
            writeAtomic(path.join(paths.working, name), supplied);
            log(`seeded ${name} from the supplied files`);
        }
        loadState();
    }

    function writeWorking(name, text) {
        ensureDirectory(paths.working);
        writeAtomic(path.join(paths.working, name), Buffer.from(text, 'utf8'));
    }

    /** Restores one file, or all of them, from the pristine copy. Returns the names restored. */
    function reset(names) {
        const restored = [];
        for (const name of names) {
            const supplied = readSupplied(name);
            if (supplied === null) {
                continue;
            }
            writeWorking(name, supplied.toString('utf8'));
            restored.push(name);
        }
        return restored;
    }

    return {
        paths,
        seed,
        reset,
        readSupplied,
        readWorking,
        writeWorking,
        hasSupplied: name => readSupplied(name) !== null,
        version: () => state.version,
        pushedAt: () => state.pushedAt,
        push() {
            state.version += 1;
            state.pushedAt = new Date().toISOString();
            saveState();
            return { version: state.version, pushedAt: state.pushedAt };
        }
    };
}

// ---------------------------------------------------------------------------
// Validation
//
// The rules here are a restatement of what the game itself does with each file, which is why they are
// worth more than a JSON syntax check: a designer who breaks a rule gets told what the game will do
// instead, not just that something is wrong.
// ---------------------------------------------------------------------------

/** Merges the three string files the way the game does: lowest priority first. */
function mergeStrings(documents) {
    const merged = {};
    for (const name of STRING_FILES) {
        const node = documents[name];
        if (isPlainObject(node) && isPlainObject(node.strings)) {
            Object.assign(merged, node.strings);
        }
    }
    return merged;
}

/** The mornings value the game would resolve for a transition, and whether it was stated. */
function resolveMornings(config, from, to) {
    const table = isPlainObject(getPath(config, 'growth.mornings')) ? config.growth.mornings : {};
    const candidates = [camelMorningKey(from, to), `${from}_to_${to}`, `${from}To${to}`];
    for (const key of candidates) {
        const value = Number(table[key]);
        if (Number.isFinite(value)) {
            return { value: Math.max(1, value), explicit: true, key };
        }
    }
    const fallback = Number(getPath(config, 'growth.defaultMorningsPerTransition'));
    return {
        value: Number.isFinite(fallback) ? Math.max(1, fallback) : 1,
        explicit: false,
        key: null
    };
}

function validateNumbers(node, rules, errors, warnings) {
    for (const rule of rules) {
        const value = getPath(node, rule.path);
        if (value === undefined) {
            warnings.push(`${rule.path} is missing; the game falls back to ${rule.fallback}.`);
            continue;
        }
        if (typeof value !== 'number' || !Number.isFinite(value)) {
            errors.push(`${rule.path} must be a number.`);
            continue;
        }
        if (rule.integer && !Number.isInteger(value)) {
            errors.push(`${rule.path} must be a whole number.`);
            continue;
        }
        if (rule.min !== undefined && value < rule.min) {
            errors.push(`${rule.path} must be at least ${rule.min}.`);
        }
        if (rule.max !== undefined && value > rule.max) {
            errors.push(`${rule.path} must be at most ${rule.max}.`);
        }
    }
}

function validateConfig(document, text, errors, warnings) {
    if (!isPlainObject(document)) {
        errors.push('config.json must contain a JSON object.');
        return;
    }

    validateNumbers(document, [
        { path: 'startDrops', integer: true, min: 0, fallback: 3 },
        { path: 'miniGame.rounds', integer: true, min: 1, fallback: 5 },
        { path: 'miniGame.choicesShown', integer: true, min: 2, fallback: 3 },
        { path: 'miniGame.totalSeconds', min: 1, fallback: 60 },
        { path: 'miniGame.minCorrectForReward', integer: true, min: 0, fallback: 3 },
        { path: 'miniGame.dropsPerCorrect', integer: true, min: 0, fallback: 1 },
        { path: 'miniGame.bonusDropsIfAllCorrect', integer: true, min: 0, fallback: 2 },
        { path: 'watering.costDrops', integer: true, min: 0, fallback: 4 },
        { path: 'thirsty.afterMorningsWithoutWater', integer: true, min: 1, fallback: 1 },
        { path: 'ui.maxAnimationMs', min: 1, fallback: 600 },
        { path: 'ui.numberCountUpMs', min: 0, fallback: 400 }
    ], errors, warnings);

    const rounds = Number(getPath(document, 'miniGame.rounds'));
    const threshold = Number(getPath(document, 'miniGame.minCorrectForReward'));
    if (Number.isFinite(rounds) && Number.isFinite(threshold) && threshold > rounds) {
        errors.push(
            `miniGame.minCorrectForReward (${threshold}) is above miniGame.rounds (${rounds}), ` +
            'so no session could ever earn a reward.'
        );
    }

    // Growth: the stage chain and the mornings that walk it.
    const stages = getPath(document, 'growth.stages');
    if (!Array.isArray(stages) || stages.length === 0) {
        errors.push('growth.stages must be a non-empty array of stage ids.');
    } else {
        const seen = new Set();
        stages.forEach((stage, index) => {
            if (!isNonEmptyString(stage)) {
                errors.push(`growth.stages[${index}] must be a non-empty string.`);
                return;
            }
            if (seen.has(stage)) {
                errors.push(`growth.stages contains "${stage}" more than once.`);
            }
            seen.add(stage);
            if (!/^[a-z0-9_]+$/.test(stage)) {
                warnings.push(
                    `growth.stages[${index}] "${stage}" is used in string keys and in mornings names; ` +
                    'lower-case letters, digits and underscores are safest.'
                );
            }
        });

        if (isPlainObject(getPath(document, 'growth.mornings'))) {
            for (let index = 0; index + 1 < stages.length; index += 1) {
                const from = stages[index];
                const to = stages[index + 1];
                if (!isNonEmptyString(from) || !isNonEmptyString(to)) {
                    continue;
                }
                const resolved = resolveMornings(document, from, to);
                if (!resolved.explicit) {
                    warnings.push(
                        `growth.mornings has no entry for ${from} -> ${to}; the game will use ` +
                        `${resolved.value} morning${resolved.value === 1 ? '' : 's'}.`
                    );
                }
            }
        } else {
            warnings.push(
                'growth.mornings is missing; every stage will take one watered morning.'
            );
        }
    }

    // Economy: can a perfect session actually pay for a watering?
    const dropsPerCorrect = Number(getPath(document, 'miniGame.dropsPerCorrect'));
    const bonus = Number(getPath(document, 'miniGame.bonusDropsIfAllCorrect'));
    const cost = Number(getPath(document, 'watering.costDrops'));
    if (Number.isFinite(rounds) && Number.isFinite(dropsPerCorrect) &&
        Number.isFinite(bonus) && Number.isFinite(cost)) {
        const best = rounds * dropsPerCorrect + bonus;
        if (cost > best) {
            warnings.push(
                `watering.costDrops (${cost}) is more than the best possible session payout ` +
                `(${best}), so the pot can never be watered.`
            );
        }
    }

    if (text.indexOf('"startDrops"') === -1 && getPath(document, 'startDrops') === undefined) {
        warnings.push('startDrops is missing; the player begins with 3 drops.');
    }
}

function validatePairs(document, errors, warnings) {
    if (!Array.isArray(document)) {
        errors.push('pairs.json must be a JSON array of word/antonym entries.');
        return;
    }

    const usable = [];
    const seenWords = new Map();

    document.forEach((entry, index) => {
        if (!isPlainObject(entry)) {
            errors.push(`pairs.json[${index}] must be an object.`);
            return;
        }

        const word = entry.word;
        const opposite = entry.opposite;
        const wrong = Array.isArray(entry.wrong) ? entry.wrong : null;

        let complete = true;
        if (!isNonEmptyString(word)) {
            errors.push(`pairs.json[${index}] needs a non-empty "word".`);
            complete = false;
        }
        if (!isNonEmptyString(opposite)) {
            errors.push(`pairs.json[${index}] needs a non-empty "opposite".`);
            complete = false;
        }
        if (wrong === null || wrong.filter(isNonEmptyString).length === 0) {
            errors.push(`pairs.json[${index}] needs at least one distractor in "wrong".`);
            complete = false;
        }
        if (!complete) {
            return;
        }

        const options = wrong.filter(isNonEmptyString);
        if (options.indexOf(opposite) !== -1) {
            warnings.push(
                `pairs.json[${index}] "${word}" lists its own antonym among the wrong answers.`
            );
        }
        if (options.indexOf(word) !== -1) {
            warnings.push(`pairs.json[${index}] "${word}" lists the word itself as a wrong answer.`);
        }
        if (new Set(options).size !== options.length) {
            warnings.push(`pairs.json[${index}] "${word}" repeats one of its wrong answers.`);
        }
        if (seenWords.has(word)) {
            warnings.push(
                `pairs.json[${index}] "${word}" repeats the word from entry ${seenWords.get(word)}.`
            );
        } else {
            seenWords.set(word, index);
        }

        usable.push(entry);
    });

    if (usable.length < 3) {
        errors.push(
            `pairs.json has ${usable.length} usable entr${usable.length === 1 ? 'y' : 'ies'}; the ` +
            'minigame asks for 5 rounds of 3 choices and needs at least 3.'
        );
    } else if (usable.length < 5) {
        warnings.push(
            `pairs.json has ${usable.length} usable entries for 5 rounds, so words will repeat ` +
            'within a session.'
        );
    }
}

function validateStringsFile(name, document, merged, config, errors, warnings) {
    if (!isPlainObject(document)) {
        errors.push(`${name} must contain a JSON object.`);
        return;
    }
    if (!isPlainObject(document.strings)) {
        errors.push(`${name} needs a top-level "strings" object.`);
        return;
    }

    for (const key of Object.keys(document.strings)) {
        const value = document.strings[key];
        if (typeof value !== 'string') {
            errors.push(`${name}: "${key}" must be a string; the game cannot display it.`);
        } else if (key.trim() === '') {
            warnings.push(`${name}: an empty key can never be read.`);
        }
    }

    if (name !== STRING_FILES[STRING_FILES.length - 1]) {
        return;
    }

    // Only the highest-priority file reports the merged result, so a designer is told once rather than
    // three times over.
    const missing = REQUIRED_STRING_KEYS.filter(key => typeof merged[key] !== 'string');
    if (missing.length > 0) {
        warnings.push(
            `The three string files together are missing ${missing.length} key` +
            `${missing.length === 1 ? '' : 's'} the game reads: ${formatList(missing)}.`
        );
    }

    const stages = getPath(config, 'growth.stages');
    if (Array.isArray(stages)) {
        const withoutLabels = stages
            .filter(stage => isNonEmptyString(stage))
            .filter(stage => typeof merged[`pot.state.${stage}`] !== 'string');
        if (withoutLabels.length > 0) {
            warnings.push(
                `No pot.state.<stage> string for: ${formatList(withoutLabels)}; the pot would show ` +
                'a missing-key marker for those stages.'
            );
        }
    }

    for (const key of Object.keys(merged)) {
        const value = merged[key];
        if (typeof value !== 'string') {
            continue;
        }
        const found = Array.from(new Set(value.match(/\{([a-zA-Z_][a-zA-Z0-9_]*)\}/g) || []));
        const required = PLACEHOLDERS[key];

        for (const token of found) {
            const bare = token.slice(1, -1);
            if (KNOWN_PLACEHOLDERS.indexOf(bare) === -1) {
                warnings.push(`"${key}" uses ${token}, which no call site fills in.`);
                continue;
            }
            if (!required || required.indexOf(bare) === -1) {
                warnings.push(
                    `"${key}" contains ${token}, but the game passes no values to that key.`
                );
            }
        }

        if (required) {
            for (const bare of required) {
                if (value.indexOf(`{${bare}}`) === -1) {
                    warnings.push(
                        `"${key}" should contain {${bare}}; without it the player never sees that value.`
                    );
                }
            }
        }
    }
}

function validateDevFile(document, warnings) {
    if (!isPlainObject(document)) {
        return;
    }
    if (document.dataServerUrl !== undefined && typeof document.dataServerUrl !== 'string') {
        warnings.push('dev.json: "dataServerUrl" should be a string.');
    } else if (typeof document.dataServerUrl === 'string' && document.dataServerUrl.trim() !== '' &&
        !/^https?:\/\//i.test(document.dataServerUrl.trim())) {
        warnings.push('dev.json: "dataServerUrl" should start with http:// or https://.');
    }
    for (const key of ['showDataSourceInfo', 'showDebugPanel']) {
        if (document[key] !== undefined && typeof document[key] !== 'boolean') {
            warnings.push(`dev.json: "${key}" should be true or false.`);
        }
    }
}

/**
 * Validates a whole content set.
 *
 * `texts` maps a managed file name to its text (or null when it is absent). Returns one result per
 * managed file: { name, valid, errors, warnings }. Pure: it touches no disk, which is what makes the
 * rules testable on their own.
 */
function validateContent(texts) {
    const documents = {};
    const parses = {};

    for (const name of MANAGED_FILES) {
        const text = typeof texts[name] === 'string' ? texts[name] : null;
        const parsed = parseJson(text);
        parses[name] = parsed;
        documents[name] = parsed.ok ? parsed.value : null;
    }

    const merged = mergeStrings(documents);
    const config = documents['config.json'];
    const results = {};

    for (const name of MANAGED_FILES) {
        const errors = [];
        const warnings = [];
        const parsed = parses[name];

        if (!parsed.ok) {
            const absent = typeof texts[name] !== 'string';
            if (absent && OPTIONAL_FILES.indexOf(name) !== -1) {
                // Optional and absent: the game falls back, so there is nothing to report.
            } else if (absent) {
                errors.push(`${name} is missing from the working copy.`);
            } else {
                errors.push(`${name} is not valid JSON: ${parsed.error}`);
            }
        } else if (name === 'config.json') {
            validateConfig(documents[name], texts[name] || '', errors, warnings);
        } else if (name === 'pairs.json') {
            validatePairs(documents[name], errors, warnings);
        } else if (STRING_FILES.indexOf(name) !== -1) {
            validateStringsFile(name, documents[name], merged, config, errors, warnings);
        } else if (name === 'dev.json') {
            validateDevFile(documents[name], warnings);
        }

        results[name] = { name, valid: errors.length === 0, errors, warnings };
    }

    return results;
}

// ---------------------------------------------------------------------------
// HTTP plumbing
// ---------------------------------------------------------------------------

function corsHeaders() {
    return {
        'Access-Control-Allow-Origin': '*',
        'Access-Control-Allow-Methods': 'GET, HEAD, POST, OPTIONS',
        'Access-Control-Allow-Headers': '*'
    };
}

function send(request, response, status, body, contentType, extraHeaders) {
    const headers = Object.assign(
        {
            'Content-Type': contentType || 'text/plain; charset=utf-8',
            'Cache-Control': 'no-store, no-cache, must-revalidate',
            Pragma: 'no-cache'
        },
        corsHeaders(),
        extraHeaders || {}
    );

    response.writeHead(status, headers);
    if (request.method === 'HEAD' || body === undefined || body === null) {
        response.end();
        return;
    }
    response.end(body);
}

function sendJson(request, response, status, value, extraHeaders) {
    send(request, response, status, JSON.stringify(value), 'application/json; charset=utf-8', extraHeaders);
}

/** True when the client's If-None-Match covers this tag. Handles weak tags and lists. */
function etagMatches(header, etag) {
    if (!header) {
        return false;
    }
    return header.split(',').some(candidate => {
        const trimmed = candidate.trim().replace(/^W\//, '');
        return trimmed === '*' || trimmed === etag;
    });
}

function etagFor(buffer) {
    return '"' + sha256(buffer).substring(0, 16) + '"';
}

function readBody(request) {
    return new Promise((resolve, reject) => {
        const chunks = [];
        let size = 0;
        let tooLarge = false;

        request.on('data', chunk => {
            size += chunk.length;
            // The rest of an oversized body is drained rather than the socket being destroyed, so the
            // client still gets a readable 413 instead of a connection reset.
            if (size > MAX_BODY_BYTES) {
                tooLarge = true;
                return;
            }
            chunks.push(chunk);
        });

        request.on('end', () => {
            if (tooLarge) {
                reject(Object.assign(new Error(`The request body is larger than ${MAX_BODY_BYTES} bytes.`),
                    { status: 413 }));
                return;
            }
            const text = Buffer.concat(chunks).toString('utf8');
            if (text.trim() === '') {
                reject(Object.assign(new Error('A JSON body is required.'), { status: 400 }));
                return;
            }
            try {
                resolve(JSON.parse(text));
            } catch (error) {
                reject(Object.assign(new Error(`The request body is not valid JSON: ${error.message}`),
                    { status: 400 }));
            }
        });

        request.on('error', error => reject(Object.assign(error, { status: 400 })));
    });
}

// ---------------------------------------------------------------------------
// Server
// ---------------------------------------------------------------------------

function createServer(options) {
    const settings = options || {};
    const paths = pathsFrom(settings);
    const quiet = settings.quiet === true;
    const log = message => {
        if (!quiet) {
            console.log(message);
        }
    };

    const store = createStore(paths, log);
    store.seed();

    // Clients holding a request open, waiting for the revision to change. A push answers them in the same
    // tick, which is the difference between "instant" and "whenever the next poll happens to fall".
    const watchers = new Set();

    /** Answers every waiting client with the revision that is current now, and says how many there were. */
    function releaseWatchers() {
        const waiting = Array.from(watchers);
        for (const watcher of waiting) {
            watcher.finish(store.version(), 'push');
        }
        return waiting.length;
    }

    /**
     * Holds one client's request open until the revision changes.
     *
     * The client says which revision it already has. If that is not the current one the answer is
     * immediate, which is also how a client that has never seen this server establishes its baseline.
     * Otherwise the request is parked and the push itself answers it, so the app hears about new content
     * in the time one round trip takes rather than whenever a timer happens to fire.
     *
     * The hold expires on its own, so a client re-issues rather than trusting a connection that may have
     * been dropped somewhere along the way. A client that hangs up is dropped from the set, so a dead
     * socket is never written to.
     */
    function awaitVersion(request, response, url) {
        const since = Number(url.searchParams.get('version'));
        const hold = clampHoldSeconds(Number(url.searchParams.get('hold')));
        const started = Date.now();

        const reply = (version, reason) => {
            sendJson(request, response, 200, {
                version,
                pushedAt: store.pushedAt(),
                changed: version !== since,
                reason,
                heldMs: Date.now() - started
            });
        };

        if (!Number.isFinite(since) || since < 0 || since !== store.version()) {
            reply(store.version(), 'now');
            return true;
        }

        let timer = null;
        const watcher = {
            finish(version, reason) {
                // Removal is the guard: whichever of the push and the timer arrives second finds the
                // watcher already gone and does nothing, so one request is never answered twice.
                if (!watchers.delete(watcher)) {
                    return;
                }
                clearTimeout(timer);
                reply(version, reason);
            }
        };

        watchers.add(watcher);
        timer = setTimeout(() => watcher.finish(store.version(), 'expired'), hold * 1000);
        response.on('close', () => {
            if (watchers.delete(watcher)) {
                clearTimeout(timer);
            }
        });

        return true;
    }

    /** The full admin view: version, hashes, and the validation of every managed file. */
    function buildState() {
        const texts = {};
        const buffers = {};
        for (const name of MANAGED_FILES) {
            const buffer = store.readWorking(name);
            buffers[name] = buffer;
            texts[name] = buffer === null ? null : buffer.toString('utf8');
        }

        const validation = validateContent(texts);
        const suppliedHashes = {};
        for (const name of MANAGED_FILES) {
            const supplied = store.readSupplied(name);
            suppliedHashes[name] = supplied === null ? null : sha256(supplied);
        }

        return {
            ok: true,
            version: store.version(),
            pushedAt: store.pushedAt(),
            directories: {
                supplied: paths.supplied,
                working: paths.working,
                state: paths.state
            },
            suppliedHashes,
            files: MANAGED_FILES.map(name => {
                const result = validation[name];
                const workingHash = buffers[name] === null ? null : sha256(buffers[name]);
                const suppliedHash = suppliedHashes[name];
                return {
                    name,
                    text: texts[name],
                    exists: buffers[name] !== null,
                    supplied: SUPPLIED_ONLY_FILES.indexOf(name) !== -1,
                    dirty: workingHash !== suppliedHash,
                    suppliedIdentical: workingHash === suppliedHash,
                    valid: result.valid,
                    errors: result.errors,
                    warnings: result.warnings
                };
            })
        };
    }

    function isManaged(name) {
        return typeof name === 'string' && MANAGED_FILES.indexOf(name) !== -1;
    }

    function requireManaged(name, request, response) {
        if (isManaged(name)) {
            return true;
        }
        sendJson(request, response, 400, {
            ok: false,
            error: `"${String(name)}" is not one of the managed files: ${MANAGED_FILES.join(', ')}.`
        });
        return false;
    }

    function handleApi(request, response, route, url) {
        if (route === '/api/state' && request.method === 'GET') {
            sendJson(request, response, 200, buildState());
            return true;
        }

        if (route === '/api/save' && request.method === 'POST') {
            readBody(request).then(body => {
                const name = body && body.name;
                if (!requireManaged(name, request, response)) {
                    return;
                }
                if (typeof body.text !== 'string') {
                    sendJson(request, response, 400, {
                        ok: false,
                        error: 'The body needs a "text" string.'
                    });
                    return;
                }

                // Validation runs against the file in the context of everything else, because the
                // merged strings and the stage list are cross-file facts.
                const texts = {};
                for (const other of MANAGED_FILES) {
                    const buffer = other === name
                        ? Buffer.from(body.text, 'utf8')
                        : store.readWorking(other);
                    texts[other] = buffer === null ? null : buffer.toString('utf8');
                }
                const result = validateContent(texts)[name];

                if (result.valid) {
                    store.writeWorking(name, body.text);
                    log(`  saved ${name} (${body.text.length} bytes)`);
                } else {
                    log(`  rejected ${name}: ${result.errors.length} error(s)`);
                }

                // An invalid file is never written, so the working copy keeps serving its last good
                // content while the designer fixes the draft in the browser.
                sendJson(request, response, 200, {
                    ok: result.valid,
                    valid: result.valid,
                    errors: result.errors,
                    warnings: result.warnings
                });
            }).catch(error => {
                sendJson(request, response, error.status || 400, { ok: false, error: error.message });
            });
            return true;
        }

        if (route === '/api/watch' && request.method === 'GET') {
            return awaitVersion(request, response, url);
        }

        if (route === '/api/push' && request.method === 'POST') {
            const state = buildState();
            const invalid = state.files.filter(file => !file.valid).map(file => file.name);
            if (invalid.length > 0) {
                sendJson(request, response, 409, {
                    ok: false,
                    error: `Cannot push while ${formatList(invalid, 3)} ${invalid.length === 1 ? 'is' : 'are'} invalid.`,
                    invalid
                });
                return true;
            }
            const pushed = store.push();
            const delivered = releaseWatchers();
            log(`  pushed version ${pushed.version}; told ${delivered} watching client(s)`);
            sendJson(request, response, 200, {
                ok: true,
                version: pushed.version,
                pushedAt: pushed.pushedAt,
                delivered
            });
            return true;
        }

        if ((route === '/api/reset' || route === '/api/resetAll') && request.method === 'POST') {
            const names = route === '/api/resetAll' ? MANAGED_FILES.slice() : null;
            if (names) {
                const restored = store.reset(names);
                log(`  reset ${restored.length} file(s) to the supplied copies`);
                sendJson(request, response, 200, buildState());
                return true;
            }
            readBody(request).then(body => {
                const name = body && body.name;
                if (!requireManaged(name, request, response)) {
                    return;
                }
                if (!store.hasSupplied(name)) {
                    sendJson(request, response, 404, {
                        ok: false,
                        error: `There is no supplied copy of ${name} to reset to.`
                    });
                    return;
                }
                store.reset([name]);
                log(`  reset ${name} to the supplied copy`);
                sendJson(request, response, 200, buildState());
            }).catch(error => {
                sendJson(request, response, error.status || 400, { ok: false, error: error.message });
            });
            return true;
        }

        return false;
    }

    function handleStaticFile(request, response, route) {
        const assets = {
            '/': { file: 'index.html', type: 'text/html; charset=utf-8' },
            '/index.html': { file: 'index.html', type: 'text/html; charset=utf-8' },
            '/admin.css': { file: 'admin.css', type: 'text/css; charset=utf-8' },
            '/admin.js': { file: 'admin.js', type: 'text/javascript; charset=utf-8' }
        };

        const asset = assets[route];
        if (!asset) {
            return false;
        }

        const file = path.join(paths.publicDirectory, asset.file);
        let content;
        try {
            content = fs.readFileSync(file);
        } catch (error) {
            send(request, response, 500, `The admin asset ${asset.file} is missing from ${paths.publicDirectory}.\n`);
            return true;
        }

        const etag = etagFor(content);
        if (etagMatches(request.headers['if-none-match'], etag)) {
            send(request, response, 304, null, asset.type, { ETag: etag });
            return true;
        }
        send(request, response, 200, content, asset.type, { ETag: etag });
        return true;
    }

    function handle(request, response) {
        const url = new URL(request.url, `http://${request.headers.host || 'localhost'}`);
        const route = decodeURIComponent(url.pathname);
        const stamp = new Date().toISOString().substring(11, 19);

        if (request.method === 'OPTIONS') {
            send(request, response, 204, null);
            return;
        }

        if (route.startsWith('/api/')) {
            if (handleApi(request, response, route, url)) {
                return;
            }
            sendJson(request, response, 404, { ok: false, error: `Unknown API route ${route}.` });
            return;
        }

        if (request.method !== 'GET' && request.method !== 'HEAD') {
            send(request, response, 405, 'Only GET, HEAD and POST are supported.\n', undefined,
                { Allow: 'GET, HEAD, POST, OPTIONS' });
            return;
        }

        if (route === '/version') {
            sendJson(request, response, 200, { version: store.version(), pushedAt: store.pushedAt() });
            log(`${stamp} 200 /version`);
            return;
        }

        if (route === '/health') {
            sendJson(request, response, 200, {
                ok: true,
                version: store.version(),
                files: MANAGED_FILES.slice()
            });
            log(`${stamp} 200 /health`);
            return;
        }

        if (route === '/files') {
            const lines = [
                'Small Pot content server',
                '',
                'Supplied (read-only): ' + paths.supplied,
                'Working copy        : ' + paths.working,
                'Version             : ' + store.version(),
                '',
                'Files:'
            ];
            for (const name of MANAGED_FILES) {
                const buffer = store.readWorking(name);
                const supplied = store.readSupplied(name);
                const status = buffer === null
                    ? 'missing'
                    : sha256(buffer) === (supplied === null ? null : sha256(supplied))
                        ? 'identical to supplied'
                        : 'edited';
                lines.push(`  /${name}  (${buffer === null ? 0 : buffer.length} bytes, ${status})`);
            }
            lines.push('');
            send(request, response, 200, lines.join('\n') + '\n');
            log(`${stamp} 200 /files`);
            return;
        }

        if (handleStaticFile(request, response, route)) {
            log(`${stamp} 200 ${route}`);
            return;
        }

        // The client builds <serverUrl>/<fileName>, so the managed files stay at the root. The name is
        // checked against the allow-list, which is also the strongest defence against a crafted path.
        const requestedName = route.startsWith('/') ? route.slice(1) : route;
        if (isManaged(requestedName)) {
            const content = store.readWorking(requestedName);
            if (content === null) {
                send(request, response, 404, `No such data file: ${requestedName}\n`);
                log(`${stamp} 404 ${route}`);
                return;
            }

            const etag = etagFor(content);
            if (etagMatches(request.headers['if-none-match'], etag)) {
                send(request, response, 304, null, 'application/json; charset=utf-8', { ETag: etag });
                log(`${stamp} 304 ${route}`);
                return;
            }

            send(request, response, 200, content, 'application/json; charset=utf-8', { ETag: etag });
            log(`${stamp} 200 ${route} (${content.length} bytes)`);
            return;
        }

        send(request, response, 403, `Only these files can be requested: ${MANAGED_FILES.join(', ')}.\n`);
        log(`${stamp} 403 ${route}`);
    }

    const server = http.createServer((request, response) => {
        try {
            handle(request, response);
        } catch (error) {
            log(`  request failed: ${error.stack || error.message}`);
            if (!response.headersSent) {
                sendJson(request, response, 500, { ok: false, error: error.message });
            } else {
                response.end();
            }
        }
    });

    return { server, store, paths, buildState, validateContent };
}

// A phone reaches this machine over the network, and every virtual adapter on a development machine -
// vEthernet (WSL), Hyper-V, VirtualBox, Docker - also holds an IPv4 address that no phone can reach. The
// names give them away, so they stay out of the list a person is being asked to read and type.
const VIRTUAL_INTERFACE = /^(vEthernet|VirtualBox|VMware|Hyper-V|Docker|docker|br-|veth|utun|tun|tap|Tailscale|ZeroTier|Loopback)/i;

function localAddresses() {
    const addresses = [];
    const interfaces = os.networkInterfaces();
    for (const name of Object.keys(interfaces)) {
        if (VIRTUAL_INTERFACE.test(name)) continue;
        for (const entry of interfaces[name] || []) {
            if (entry.family === 'IPv4' && !entry.internal) {
                addresses.push(entry.address);
            }
        }
    }

    // Household and small-office ranges first: those are what a router hands out, and the ones a phone on
    // the same wifi is most likely to reach.
    return addresses.sort((left, right) => addressRank(left) - addressRank(right));
}

function addressRank(address) {
    if (address.startsWith('192.168.')) return 0;
    if (address.startsWith('10.')) return 1;
    if (address.startsWith('172.')) return 2;
    return 3;
}

function startFromCommandLine() {
    const port = Number(process.argv[2] || process.env.PORT || 8787);
    const app = createServer();

    app.server.on('error', error => {
        if (error.code === 'EADDRINUSE') {
            console.error(`Port ${port} is already in use. Pass another one: node server/server.js 8788`);
        } else {
            console.error(error.message);
        }
        process.exit(1);
    });

    app.server.listen(port, () => {
        const bound = app.server.address().port;
        console.log(`Small Pot content server listening on port ${bound}`);
        console.log(`Supplied (read-only) : ${app.paths.supplied}`);
        console.log(`Working copy         : ${app.paths.working}`);
        console.log(`Content version      : ${app.store.version()}`);
        console.log('');
        console.log('  Admin page      : http://127.0.0.1:' + bound + '/');
        const networkAddresses = localAddresses();
        for (let index = 0; index < networkAddresses.length; index++) {
            const hint = index === 0 && networkAddresses.length > 1 ? '   <- the one a phone should use' : '';
            console.log('  On the network  : http://' + networkAddresses[index] + ':' + bound + '/' + hint);
        }
        console.log('');
        console.log('  Over USB, with the phone attached, run: adb reverse tcp:' + bound + ' tcp:' + bound);
        console.log('  then use http://127.0.0.1:' + bound + ' inside the app.');
        console.log('');
        console.log('Edit content in the admin page, press Push, and the app reloads on its next poll.');
    });
}

module.exports = {
    createServer,
    pathsFrom,
    MANAGED_FILES,
    SUPPLIED_ONLY_FILES,
    STRING_FILES,
    OPTIONAL_FILES,
    REQUIRED_STRING_KEYS,
    PLACEHOLDERS,
    mergeStrings,
    resolveMornings,
    validateContent,
    camelMorningKey
};

if (require.main === module) {
    startFromCommandLine();
}
