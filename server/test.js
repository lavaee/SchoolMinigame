#!/usr/bin/env node
/*
 * Tests for the content server.
 *
 *   node server/test.js          (from anywhere; the paths are relative to this file)
 *
 * The server is required in-process and listened on port 0, so the suite never binds a fixed port and
 * never spawns anything. It writes into a temporary directory of its own, which means running the
 * tests cannot disturb the content a designer is editing.
 *
 * Exit code 0 when everything passed, 1 with a readable report when something did not.
 */

'use strict';

const http = require('http');
const fs = require('fs');
const os = require('os');
const path = require('path');
const crypto = require('crypto');

const server = require('./server.js');

const dataDirectory = path.resolve(__dirname, '..', 'data');
const MANAGED_FILES = server.MANAGED_FILES;

let passed = 0;
const failures = [];
let currentSection = '';

function section(title) {
    currentSection = title;
    console.log('\n' + title);
}

function check(name, condition, detail) {
    if (condition) {
        passed += 1;
        console.log('  ok   ' + name);
        return true;
    }
    failures.push(`[${currentSection}] ${name}${detail ? ' - ' + detail : ''}`);
    console.log('  FAIL ' + name + (detail ? ' - ' + detail : ''));
    return false;
}

function equal(name, actual, expected) {
    return check(name, actual === expected, `expected ${JSON.stringify(expected)}, got ${JSON.stringify(actual)}`);
}

/** For the shape rules, where the numbers are fractions and exact equality is the wrong test. */
function about(name, actual, expected) {
    return check(name, Math.abs(actual - expected) < 1e-9, `expected ${expected}, got ${actual}`);
}

function sha256(buffer) {
    return crypto.createHash('sha256').update(buffer).digest('hex');
}

function suppliedBytes(name) {
    return fs.readFileSync(path.join(dataDirectory, name));
}

let suppliedTexts = null;

function baseline() {
    if (suppliedTexts === null) {
        suppliedTexts = {};
        for (const name of MANAGED_FILES) {
            suppliedTexts[name] = fs.readFileSync(path.join(dataDirectory, name), 'utf8');
        }
    }
    return Object.assign({}, suppliedTexts);
}

/** A full content set with one file replaced, for exercising the rules on their own. */
function withFile(name, value) {
    const texts = baseline();
    texts[name] = typeof value === 'string' ? value : JSON.stringify(value, null, 2) + '\n';
    return texts;
}

function warningsOf(result) {
    return result.warnings.join(' | ');
}

function errorsOf(result) {
    return result.errors.join(' | ');
}

function mentions(list, needle) {
    return list.some(entry => entry.toLowerCase().indexOf(needle.toLowerCase()) !== -1);
}

// ---------------------------------------------------------------------------
// HTTP helpers
// ---------------------------------------------------------------------------

let port = 0;

function request(method, requestPath, options) {
    const settings = options || {};
    const body = settings.body === undefined ? null : settings.body;
    const headers = Object.assign({}, settings.headers || {});
    if (body !== null && headers['Content-Type'] === undefined) {
        headers['Content-Type'] = 'application/json';
    }
    if (headers['Content-Type'] === null) {
        delete headers['Content-Type'];
    }

    return new Promise((resolve, reject) => {
        const call = http.request({ host: '127.0.0.1', port, method, path: requestPath, headers }, response => {
            const chunks = [];
            response.on('data', chunk => chunks.push(chunk));
            response.on('end', () => resolve({
                status: response.statusCode,
                headers: response.headers,
                body: Buffer.concat(chunks)
            }));
        });
        call.on('error', reject);
        if (body !== null) {
            call.write(body);
        }
        call.end();
    });
}

function getJson(requestPath, headers) {
    return request('GET', requestPath, { headers }).then(response => ({
        status: response.status,
        headers: response.headers,
        body: response.body,
        json: (() => {
            try {
                return JSON.parse(response.body.toString('utf8'));
            } catch (error) {
                return null;
            }
        })()
    }));
}

function postJson(requestPath, value) {
    return request('POST', requestPath, { body: JSON.stringify(value) }).then(response => ({
        status: response.status,
        headers: response.headers,
        body: response.body,
        json: (() => {
            try {
                return JSON.parse(response.body.toString('utf8'));
            } catch (error) {
                return null;
            }
        })()
    }));
}

function sleep(ms) {
    return new Promise(resolve => setTimeout(resolve, ms));
}

// ---------------------------------------------------------------------------
// The suite
// ---------------------------------------------------------------------------

async function run(workingDirectory, statePath) {
    section('Serving the contract the app depends on');

    const page = await request('GET', '/');
    const pageText = page.body.toString('utf8');
    equal('GET / is the admin page', page.status, 200);
    check('GET / is html', /text\/html/.test(page.headers['content-type'] || ''), page.headers['content-type']);
    check('GET / declares lang="en"', pageText.indexOf('<html lang="en">') !== -1);
    check('GET / loads admin.js and admin.css',
        pageText.indexOf('/admin.js') !== -1 && pageText.indexOf('/admin.css') !== -1);
    check('GET / is never cached', /no-store/.test(page.headers['cache-control'] || ''), page.headers['cache-control']);
    check('GET / allows any origin', page.headers['access-control-allow-origin'] === '*');
    // The desk usually has no internet, so the page has to be complete as served.
    check('the page loads nothing from the network', !/(src|href)="https?:/.test(pageText));

    const css = await request('GET', '/admin.css');
    equal('GET /admin.css', css.status, 200);
    check('GET /admin.css is css', /text\/css/.test(css.headers['content-type'] || ''));
    check('GET /admin.css is never cached', /no-store/.test(css.headers['cache-control'] || ''));
    check('GET /admin.css has no external imports',
        css.body.toString('utf8').indexOf('@import') === -1);
    check('the stylesheet fetches nothing external',
        !/url\(\s*['"]?https?:/.test(css.body.toString('utf8')));

    const js = await request('GET', '/admin.js');
    equal('GET /admin.js', js.status, 200);
    check('GET /admin.js is javascript', /javascript/.test(js.headers['content-type'] || ''));
    check('GET /admin.js draws the silhouettes',
        js.body.toString('utf8').indexOf('PlantVisuals.cs') !== -1);

    // Without a browser in the loop, the cheapest real check on the page is that every element the
    // script reaches for is one the markup actually defines.
    const jsText = js.body.toString('utf8');
    const idsUsed = Array.from(new Set(
        (jsText.match(/getElementById\('[^']+'\)/g) || [])
            .map(match => match.slice("getElementById('".length, -2))
    ));
    check('admin.js injects no external script',
        jsText.indexOf('<script') === -1 && jsText.indexOf('cdn.') === -1);
    check('admin.js only looks up ids the page defines',
        idsUsed.length > 0 && idsUsed.every(id => pageText.indexOf('id="' + id + '"') !== -1),
        idsUsed.filter(id => pageText.indexOf('id="' + id + '"') === -1).join(', '));
    check('the page defines no id the script ignores',
        ['version', 'pushed-at', 'edit-state', 'banner', 'rail', 'editor', 'preview', 'push', 'reset-all']
            .every(id => idsUsed.indexOf(id) !== -1));

    const listing = await request('GET', '/files');
    const listingText = listing.body.toString('utf8');
    equal('GET /files', listing.status, 200);
    check('GET /files lists every managed file',
        MANAGED_FILES.every(name => listingText.indexOf('/' + name) !== -1));

    const version = await getJson('/version');
    equal('GET /version', version.status, 200);
    equal('GET /version starts at version 1', version.json.version, 1);
    equal('GET /version has no push yet', version.json.pushedAt, null);

    const health = await getJson('/health');
    equal('GET /health', health.status, 200);
    equal('GET /health is ok', health.json.ok, true);
    check('GET /health lists the six files',
        Array.isArray(health.json.files) && health.json.files.length === 6 &&
        MANAGED_FILES.every(name => health.json.files.indexOf(name) !== -1));
    equal('GET /health reports the version', health.json.version, 1);

    section('Serving the working copy of the supplied files');

    for (const name of MANAGED_FILES) {
        const response = await request('GET', '/' + name);
        const expected = suppliedBytes(name);
        equal('GET /' + name, response.status, 200);
        check('GET /' + name + ' is byte-identical to the supplied file',
            Buffer.compare(response.body, expected) === 0);
        check('GET /' + name + ' is json',
            /application\/json/.test(response.headers['content-type'] || ''));
        check('GET /' + name + ' is never cached',
            /no-store/.test(response.headers['cache-control'] || ''));
        check('GET /' + name + ' has an ETag', typeof response.headers.etag === 'string');

        const cached = await request('GET', '/' + name, { headers: { 'If-None-Match': response.headers.etag } });
        equal('GET /' + name + ' honours If-None-Match', cached.status, 304);
        equal('GET /' + name + ' sends no body on 304', cached.body.length, 0);
    }

    section('Refusing anything that is not a managed file');

    const unknown = await request('GET', '/package.json');
    equal('GET /package.json is refused', unknown.status, 403);
    check('GET /package.json leaks nothing',
        unknown.body.toString('utf8').indexOf('"name"') === -1);

    const traversal = await request('GET', '/%2e%2e%2fdata%2fconfig.json');
    equal('an encoded ../ is refused', traversal.status, 403);
    check('an encoded ../ leaks nothing',
        traversal.body.toString('utf8').indexOf('startDrops') === -1);

    const plainTraversal = await request('GET', '/../../data/config.json');
    check('a plain ../ is refused',
        plainTraversal.status === 403 || plainTraversal.status === 404,
        'status ' + plainTraversal.status);

    const wrongMethod = await request('POST', '/health', { body: '{}' });
    equal('POST /health is not allowed', wrongMethod.status, 405);
    check('405 says what is allowed', (wrongMethod.headers.allow || '').indexOf('GET') !== -1);

    const options = await request('OPTIONS', '/config.json');
    equal('OPTIONS is answered', options.status, 204);
    check('OPTIONS allows POST', /POST/.test(options.headers['access-control-allow-methods'] || ''));

    section('The admin state');

    const state = await getJson('/api/state');
    equal('GET /api/state', state.status, 200);
    equal('state has six files', state.json.files.length, 6);
    equal('state reports the version', state.json.version, 1);
    equal('state reports the pristine hash of config.json',
        state.json.suppliedHashes['config.json'], sha256(suppliedBytes('config.json')));

    const pristine = state.json.files.filter(file => file.suppliedIdentical && !file.dirty);
    equal('every file starts identical to the supplied copy', pristine.length, 6);

    const cleanValidation = state.json.files.filter(file => file.valid && file.errors.length === 0);
    equal('the supplied content has no validation errors', cleanValidation.length, 6);

    const noisy = state.json.files.filter(file => file.warnings.length > 0);
    check('the supplied content produces no warnings either', noisy.length === 0,
        noisy.map(file => file.name + ': ' + warningsOf(file)).join(' || '));

    const suppliedFlag = state.json.files.filter(file => file.supplied).map(file => file.name);
    equal('the four employer files are marked as supplied', suppliedFlag.length, 4);
    check('dev.json is not marked as supplied',
        state.json.files.find(file => file.name === 'dev.json').supplied === false);

    section('Saving: valid, invalid and unparseable');

    const broken = await postJson('/api/save', { name: 'config.json', text: '{ "startDrops": ' });
    equal('saving broken JSON is answered', broken.status, 200);
    equal('saving broken JSON is not ok', broken.json.ok, false);
    equal('saving broken JSON is invalid', broken.json.valid, false);
    check('the error explains the syntax', mentions(broken.json.errors, 'not valid JSON'),
        errorsOf(broken.json));

    const afterBroken = await request('GET', '/config.json');
    check('broken JSON left the working copy untouched',
        Buffer.compare(afterBroken.body, suppliedBytes('config.json')) === 0);

    const badConfig = JSON.parse(baseline()['config.json']);
    badConfig.miniGame.minCorrectForReward = 99;
    const rejected = await postJson('/api/save', { name: 'config.json', text: JSON.stringify(badConfig, null, 2) });
    equal('a rule violation is not ok', rejected.json.ok, false);
    check('the error names the rule', mentions(rejected.json.errors, 'minCorrectForReward'),
        errorsOf(rejected.json));

    const afterRejected = await request('GET', '/config.json');
    check('a rejected save left the working copy untouched',
        Buffer.compare(afterRejected.body, suppliedBytes('config.json')) === 0);

    const goodConfig = JSON.parse(fs.readFileSync(path.join(dataDirectory, 'config.json'), 'utf8'));
    goodConfig.startDrops = 7;
    const saved = await postJson('/api/save', { name: 'config.json', text: JSON.stringify(goodConfig, null, 2) });
    equal('a valid save is ok', saved.json.ok, true);
    equal('a valid save reports no errors', saved.json.errors.length, 0);

    const afterSave = await getJson('/config.json');
    equal('the working copy now serves the edit', afterSave.json.startDrops, 7);

    const dirtyState = await getJson('/api/state');
    const configFile = dirtyState.json.files.find(file => file.name === 'config.json');
    equal('the edited file is marked dirty', configFile.dirty, true);
    equal('the edited file is no longer identical to the supplied copy', configFile.suppliedIdentical, false);
    equal('the other files are still pristine',
        dirtyState.json.files.filter(file => file.suppliedIdentical).length, 5);

    section('Rejecting bad requests');

    const unknownName = await postJson('/api/save', { name: 'secrets.json', text: '{}' });
    equal('an unknown name is a 400', unknownName.status, 400);
    check('the 400 lists the managed files',
        (unknownName.json.error || '').indexOf('config.json') !== -1, unknownName.json.error);

    const malformed = await request('POST', '/api/save', { body: '{"name": "config.json", ' });
    equal('a malformed body is a 400', malformed.status, 400);

    const empty = await request('POST', '/api/save', { body: '' });
    equal('an empty body is a 400', empty.status, 400);

    const missingText = await postJson('/api/save', { name: 'config.json' });
    equal('a missing text field is a 400', missingText.status, 400);

    const oversized = await request('POST', '/api/save', {
        body: JSON.stringify({ name: 'strings.json', text: 'x'.repeat(3 * 1024 * 1024) })
    });
    equal('an oversized body is a 413', oversized.status, 413);

    const badReset = await postJson('/api/reset', { name: 'nope.json' });
    equal('resetting an unknown name is a 400', badReset.status, 400);

    section('Pushing a version');

    const push = await postJson('/api/push', {});
    equal('POST /api/push', push.status, 200);
    equal('push bumps the version', push.json.version, 2);
    check('push records when', typeof push.json.pushedAt === 'string' &&
        !Number.isNaN(Date.parse(push.json.pushedAt)), push.json.pushedAt);

    const versionAfterPush = await getJson('/version');
    equal('GET /version reports the new number', versionAfterPush.json.version, 2);
    equal('GET /version reports the push time', versionAfterPush.json.pushedAt, push.json.pushedAt);

    const healthAfterPush = await getJson('/health');
    equal('GET /health reports the new number too', healthAfterPush.json.version, 2);

    section('Refusing to push invalid content');

    // The working copy can be broken by something other than this API - a text editor, say - so the
    // push path has to check the stored files, not just remember what it last accepted.
    const configPath = path.join(workingDirectory, 'config.json');
    const goodText = fs.readFileSync(configPath, 'utf8');
    fs.writeFileSync(configPath, '{ "startDrops": ', 'utf8');

    const refused = await postJson('/api/push', {});
    equal('push refuses while a file is invalid', refused.status, 409);
    check('the refusal says why', mentions([refused.json.error || ''], 'config.json'), refused.json.error);
    check('the refusal lists the invalid file',
        Array.isArray(refused.json.invalid) && refused.json.invalid.indexOf('config.json') !== -1);

    const versionAfterRefusal = await getJson('/version');
    equal('a refused push does not bump the version', versionAfterRefusal.json.version, 2);

    fs.writeFileSync(configPath, goodText, 'utf8');

    section('Resetting to the supplied copies');

    const resetOne = await postJson('/api/reset', { name: 'config.json' });
    equal('POST /api/reset', resetOne.status, 200);
    check('reset returns the fresh state', Array.isArray(resetOne.json.files) && resetOne.json.files.length === 6);
    const resetFile = resetOne.json.files.find(file => file.name === 'config.json');
    equal('the reset file is identical to the supplied copy again', resetFile.suppliedIdentical, true);
    equal('the reset file is not dirty', resetFile.dirty, false);

    const afterReset = await request('GET', '/config.json');
    check('reset restores the pristine bytes',
        Buffer.compare(afterReset.body, suppliedBytes('config.json')) === 0);

    const strings = JSON.parse(fs.readFileSync(path.join(dataDirectory, 'strings.json'), 'utf8'));
    strings.strings['game.correct'] = 'edited by the test';
    await postJson('/api/save', { name: 'strings.json', text: JSON.stringify(strings, null, 2) });
    const editedStrings = await getJson('/strings.json');
    check('the strings edit is served', editedStrings.body.toString('utf8').indexOf('edited by the test') !== -1);

    const resetAll = await postJson('/api/resetAll', {});
    equal('POST /api/resetAll', resetAll.status, 200);
    equal('everything is pristine after resetAll',
        resetAll.json.files.filter(file => file.suppliedIdentical).length, 6);

    for (const name of MANAGED_FILES) {
        const response = await request('GET', '/' + name);
        check('resetAll restored the pristine ' + name,
            Buffer.compare(response.body, suppliedBytes(name)) === 0);
    }

    section('Optional files may be absent');

    fs.unlinkSync(path.join(workingDirectory, 'dev.json'));
    const withoutDev = await getJson('/api/state');
    const devFile = withoutDev.json.files.find(file => file.name === 'dev.json');
    equal('an absent dev.json is reported as absent', devFile.exists, false);
    equal('an absent dev.json is still valid', devFile.valid, true);
    equal('an absent dev.json has no errors', devFile.errors.length, 0);
    const fetchMissing = await request('GET', '/dev.json');
    equal('an absent dev.json is a 404', fetchMissing.status, 404);

    await postJson('/api/resetAll', {});
    const restoredDev = await request('GET', '/dev.json');
    check('resetAll brings the optional file back',
        Buffer.compare(restoredDev.body, suppliedBytes('dev.json')) === 0);

    section('Validation rules, on their own');

    const pristineSet = baseline();

    const duplicateStages = JSON.parse(pristineSet['config.json']);
    duplicateStages.growth.stages = ['seed', 'sprout', 'sprout'];
    const duplicateResult = server.validateContent(withFile('config.json', duplicateStages))['config.json'];
    check('duplicate stages are an error', mentions(duplicateResult.errors, 'more than once'),
        errorsOf(duplicateResult));

    const missingMornings = JSON.parse(pristineSet['config.json']);
    delete missingMornings.growth.mornings.sproutToPlant;
    const missingResult = server.validateContent(withFile('config.json', missingMornings))['config.json'];
    check('a missing mornings entry is only a warning',
        missingResult.valid && mentions(missingResult.warnings, 'sprout -> plant'),
        warningsOf(missingResult));

    const snakeMornings = JSON.parse(pristineSet['config.json']);
    snakeMornings.growth.mornings = { seed_to_sprout: 2, sprout_to_plant: 1, plant_to_bloom: 1 };
    const snakeResult = server.validateContent(withFile('config.json', snakeMornings))['config.json'];
    check('snake_case mornings names are accepted', snakeResult.warnings.length === 0,
        warningsOf(snakeResult));

    const expensiveWater = JSON.parse(pristineSet['config.json']);
    expensiveWater.watering.costDrops = 40;
    const expensiveResult = server.validateContent(withFile('config.json', expensiveWater))['config.json'];
    check('water costing more than a perfect session is a warning',
        expensiveResult.valid && mentions(expensiveResult.warnings, 'best possible session'),
        warningsOf(expensiveResult));

    const frozenUi = JSON.parse(pristineSet['config.json']);
    frozenUi.ui.maxAnimationMs = 0;
    const frozenResult = server.validateContent(withFile('config.json', frozenUi))['config.json'];
    check('a zero animation budget is an error',
        !frozenResult.valid && mentions(frozenResult.errors, 'maxAnimationMs'),
        errorsOf(frozenResult));

    const emptyStages = JSON.parse(pristineSet['config.json']);
    emptyStages.growth.stages = [];
    const emptyResult = server.validateContent(withFile('config.json', emptyStages))['config.json'];
    check('an empty stage list is an error',
        !emptyResult.valid && mentions(emptyResult.errors, 'non-empty array'),
        errorsOf(emptyResult));

    const twoPairs = JSON.parse(pristineSet['pairs.json']).slice(0, 2);
    const fewPairs = server.validateContent(withFile('pairs.json', twoPairs))['pairs.json'];
    check('fewer than three usable pairs is an error',
        !fewPairs.valid && mentions(fewPairs.errors, 'usable'), errorsOf(fewPairs));

    const duplicatePairs = JSON.parse(pristineSet['pairs.json']);
    duplicatePairs[1].word = duplicatePairs[0].word;
    const duplicatePairResult = server.validateContent(withFile('pairs.json', duplicatePairs))['pairs.json'];
    check('a repeated word is a warning',
        duplicatePairResult.valid && mentions(duplicatePairResult.warnings, 'repeats the word'),
        warningsOf(duplicatePairResult));

    const selfDistractor = JSON.parse(pristineSet['pairs.json']);
    selfDistractor[0].wrong = [selfDistractor[0].opposite, 'x'];
    const selfResult = server.validateContent(withFile('pairs.json', selfDistractor))['pairs.json'];
    check('listing the antonym as a distractor is a warning',
        mentions(selfResult.warnings, 'own antonym'), warningsOf(selfResult));

    const notAnArray = server.validateContent(withFile('pairs.json', '{"a":1}'))['pairs.json'];
    check('pairs.json must be an array',
        !notAnArray.valid && mentions(notAnArray.errors, 'must be a JSON array'), errorsOf(notAnArray));

    const nonString = JSON.parse(pristineSet['strings.json']);
    nonString.strings['game.correct'] = 12;
    const nonStringResult = server.validateContent(withFile('strings.json', nonString))['strings.json'];
    check('a non-string value is an error',
        !nonStringResult.valid && mentions(nonStringResult.errors, 'must be a string'),
        errorsOf(nonStringResult));

    const noWrapper = server.validateContent(withFile('strings.json', '{"version":1}'))['strings.json'];
    check('a missing strings object is an error',
        !noWrapper.valid && mentions(noWrapper.errors, 'top-level'), errorsOf(noWrapper));

    const missingKey = JSON.parse(pristineSet['strings-base.json']);
    delete missingKey.strings['hud.drops'];
    const missingKeyResult = server.validateContent(withFile('strings-base.json', missingKey));
    check('a key missing from all three files is a warning',
        mentions(missingKeyResult['strings.json'].warnings, 'hud.drops'),
        warningsOf(missingKeyResult['strings.json']));
    check('the merged warning is reported once, on the winning file',
        missingKeyResult['strings-base.json'].warnings.length === 0 &&
        missingKeyResult['strings-extra.json'].warnings.length === 0);

    const noStageString = JSON.parse(pristineSet['strings-base.json']);
    delete noStageString.strings['pot.state.sprout'];
    const stageStrings = server.validateContent(withFile('strings-base.json', noStageString));
    check('a stage with no pot.state string is a warning',
        mentions(stageStrings['strings.json'].warnings, 'pot.state.sprout'),
        warningsOf(stageStrings['strings.json']));

    const noPlaceholder = JSON.parse(pristineSet['strings-base.json']);
    noPlaceholder.strings['water.cost'] = 'cost';
    const placeholderResult = server.validateContent(withFile('strings-base.json', noPlaceholder));
    check('a missing {n} is a warning',
        mentions(placeholderResult['strings.json'].warnings, 'water.cost'),
        warningsOf(placeholderResult['strings.json']));

    // action.water is only in the base file, so an edit there is what the game actually reads.
    const strayPlaceholder = JSON.parse(pristineSet['strings-base.json']);
    strayPlaceholder.strings['action.water'] = 'water {n}';
    const strayResult = server.validateContent(withFile('strings-base.json', strayPlaceholder));
    check('a placeholder no call site fills is a warning',
        mentions(strayResult['strings.json'].warnings, 'passes no values'),
        warningsOf(strayResult['strings.json']));

    const devWrongTypes = { dataServerUrl: 12, showDebugPanel: 'yes' };
    const devResult = server.validateContent(withFile('dev.json', devWrongTypes))['dev.json'];
    check('dev.json type problems are warnings, not errors',
        devResult.valid && devResult.warnings.length === 2, warningsOf(devResult));

    const withoutDevFile = baseline();
    delete withoutDevFile['dev.json'];
    const missingDev = server.validateContent(withoutDevFile)['dev.json'];
    check('an absent dev.json is fine', missingDev.valid && missingDev.errors.length === 0,
        errorsOf(missingDev));

    const withoutExtra = baseline();
    delete withoutExtra['strings-extra.json'];
    const missingExtra = server.validateContent(withoutExtra);
    check('an absent strings-extra.json is fine',
        missingExtra['strings-extra.json'].valid && missingExtra['strings-extra.json'].errors.length === 0);

    const brokenDev = server.validateContent(withFile('dev.json', '{'))['dev.json'];
    check('a malformed dev.json is still an error',
        !brokenDev.valid && mentions(brokenDev.errors, 'not valid JSON'), errorsOf(brokenDev));

    section('The preview mirrors the game');

    const preview = require('./public/admin.js');

    const seed = preview.stageVisual('seed', 0, 4);
    about('seed is short', seed.height, 0.10);
    equal('seed has no leaf pairs', seed.leafPairs, 0);
    about('seed sits in dry-ish soil', seed.soil, 0.10);

    const sprout = preview.stageVisual('sprout', 1, 4);
    about('sprout height', sprout.height, 0.35);
    equal('sprout has one leaf pair', sprout.leafPairs, 1);
    about('sprout leaf tilt', sprout.tilt, 6);
    about('sprout soil dryness', sprout.soil, 0.12);

    const plant = preview.stageVisual('plant', 2, 4);
    about('plant height', plant.height, 0.70);
    equal('plant has two leaf pairs', plant.leafPairs, 2);
    about('plant leaf tilt', plant.tilt, 0);
    equal('plant has no blossom', plant.flower, false);
    about('plant soil dryness', plant.soil, 0.14);

    const bloom = preview.stageVisual('bloom', 3, 4);
    about('bloom height', bloom.height, 1.00);
    equal('bloom has two leaf pairs', bloom.leafPairs, 2);
    about('bloom leaf tilt', bloom.tilt, -4);
    equal('bloom has a blossom', bloom.flower, true);
    about('bloom soil dryness', bloom.soil, 0.14);

    const unknownFirst = preview.stageVisual('fruit', 0, 3);
    const unknownMiddle = preview.stageVisual('fruit', 1, 3);
    const unknownLast = preview.stageVisual('fruit', 2, 3);
    about('an unknown first stage is drawn from its position', unknownFirst.height, 0.16);
    equal('an unknown first stage has no leaves', unknownFirst.leafPairs, 0);
    about('an unknown middle stage is half way up', unknownMiddle.height, 0.605);
    equal('an unknown middle stage grows leaves', unknownMiddle.leafPairs, 2);
    about('an unknown last stage is the tallest', unknownLast.height, 1.05);
    equal('an unknown last stage blossoms', unknownLast.flower, true);
    about('an unknown stage uses the parametric soil dryness', unknownMiddle.soil, 0.14);

    const thirsty = preview.potVisual('sprout', 1, 4, { thirsty: true });
    about('thirst adds 26 degrees of droop', thirsty.tilt, 32);
    about('thirst shortens the plant by 8%', thirsty.height, 0.35 * 0.92);
    about('thirst dries the soil out', thirsty.soil, 0.90);
    check('thirst fades the leaves', thirsty.leaf !== preview.PALETTE.leaf);

    const watered = preview.potVisual('sprout', 1, 4, { watered: true });
    about('watering soaks the soil', watered.soil, 0.05);
    about('watering does not change the height', watered.height, 0.35);
    check('watering does not change the leaf colour', watered.leaf === preview.PALETTE.leaf);

    const drawings = ['seed', 'sprout', 'plant', 'bloom']
        .map((id, index) => preview.silhouetteMarkup(id, index, 4, {}));
    check('the four stages are four different drawings', new Set(drawings).size === 4);
    check('the thirsty drawing differs from the healthy one',
        preview.silhouetteMarkup('plant', 2, 4, { thirsty: true }) !==
        preview.silhouetteMarkup('plant', 2, 4, {}));
    check('the thirsty drawing shows a struck-through droplet',
        preview.silhouetteMarkup('plant', 2, 4, { thirsty: true }).indexOf('#C0503A') !== -1);
    check('the watered drawing shows a droplet',
        preview.silhouetteMarkup('plant', 2, 4, { watered: true })
            .indexOf(preview.PALETTE.accent) !== -1);
    check('the seed is a shell with no stem',
        preview.silhouetteMarkup('seed', 0, 4, {}).indexOf('<line') === -1);
    check('the bloom is drawn with five petals',
        (preview.silhouetteMarkup('bloom', 3, 4, {}).match(/#F3A3C3/g) || []).length >= 5);
    check('every drawing uses the palette pot colour',
        drawings.every(markup => markup.indexOf(preview.PALETTE.pot) !== -1));

    const previewConfig = JSON.parse(baseline()['config.json']);
    const totals = preview.growthTotals(previewConfig);
    equal('the chain walks the configured stages', totals.stages.length, 4);
    equal('full bloom takes five watered mornings', totals.total, 5);
    equal('one full growth costs twenty drops', totals.total * preview.economy(previewConfig).cost, 20);

    const money = preview.economy(previewConfig);
    equal('the best session pays seven drops', money.best, 7);
    equal('the threshold guarantees three drops', money.guaranteed, 3);
    about('a watering is four sevenths of a perfect session', money.sessionsPerWatering, 4 / 7);

    section('Merge order and morning resolution');

    const merged = server.mergeStrings({
        'strings-extra.json': { strings: { 'a.only.extra': 'extra', 'shared': 'extra' } },
        'strings-base.json': { strings: { 'a.only.base': 'base', 'shared': 'base' } },
        'strings.json': { strings: { 'a.only.strings': 'strings', 'shared': 'strings' } }
    });
    equal('the supplied file wins', merged.shared, 'strings');
    equal('the base file fills the gaps', merged['a.only.base'], 'base');
    equal('the extra file fills the rest', merged['a.only.extra'], 'extra');
    equal('the override file is not required to be complete', merged['a.only.strings'], 'strings');

    const mornings = {
        growth: {
            stages: ['a', 'b', 'c', 'd'],
            mornings: { aToB: 3, b_to_c: 4, cTod: 5 }
        }
    };
    equal('camel case is read', server.resolveMornings(mornings, 'a', 'b').value, 3);
    equal('snake case is read', server.resolveMornings(mornings, 'b', 'c').value, 4);
    equal('the third spelling is read', server.resolveMornings(mornings, 'c', 'd').value, 5);
    equal('a missing pair falls back to one', server.resolveMornings(mornings, 'd', 'e').value, 1);
    equal('a stated pair is flagged as explicit',
        server.resolveMornings(mornings, 'a', 'b').explicit, true);
    equal('a fallback is flagged as not explicit',
        server.resolveMornings(mornings, 'd', 'e').explicit, false);
    equal('defaultMorningsPerTransition is honoured',
        server.resolveMornings({ growth: { defaultMorningsPerTransition: 4 } }, 'x', 'y').value, 4);
    equal('zero mornings is clamped to one',
        server.resolveMornings({ growth: { mornings: { aToB: 0 } } }, 'a', 'b').value, 1);

    section('Telling a watching client the moment content changes');

    // A client that has never seen this server is answered straight away: that answer is also the baseline
    // it goes on to watch from.
    const firstWatch = await getJson('/api/watch?version=-1');
    equal('a watch with no version is answered at once', firstWatch.status, 200);
    equal('  naming the current revision', firstWatch.json.version, (await getJson('/version')).json.version);

    // The real thing: a watch at the current revision is held, and a push is what releases it.
    const watchingFrom = firstWatch.json.version;
    const watchStartedAt = Date.now();
    let watchAnsweredAt = null;
    const held = request('GET', `/api/watch?version=${watchingFrom}&hold=15`).then(response => {
        watchAnsweredAt = Date.now();
        return response;
    });

    await sleep(400);
    equal('a watch at the current revision is held open', watchAnsweredAt, null);

    const pushedForWatchers = await postJson('/api/push', {});
    const released = await held;
    const latency = watchAnsweredAt - watchStartedAt;
    const releasedJson = JSON.parse(released.body.toString('utf8'));
    check('the push answers the watch that was waiting',
        released.status === 200 && releasedJson.changed === true, `status ${released.status}`);
    equal('  with the revision that push created', releasedJson.version, pushedForWatchers.json.version);
    check('  inside a round trip rather than a poll interval', latency < 1500, `took ${latency} ms`);
    equal('  and the push says how many clients it told', pushedForWatchers.json.delivered, 1);

    const pushAlone = await postJson('/api/push', {});
    equal('a push with nobody watching says nobody was told', pushAlone.json.delivered, 0);

    // The hold expires by itself, so a client re-issues rather than trusting a connection that may have
    // been dropped. Two seconds is the shortest hold the server accepts.
    const expireStartedAt = Date.now();
    const expired = await request('GET', `/api/watch?version=${pushAlone.json.version}&hold=2`);
    const waited = Date.now() - expireStartedAt;
    const expiredJson = JSON.parse(expired.body.toString('utf8'));
    check('a watch that nothing happens to answers when its hold runs out', waited >= 1900, `waited ${waited} ms`);
    equal('  on the same revision', expiredJson.version, pushAlone.json.version);
    equal('  saying nothing changed', expiredJson.changed, false);
    equal('  and why it answered', expiredJson.reason, 'expired');

    // A client that hangs up while waiting must be dropped, or a dead socket gets written to and the
    // server cannot shut down. The count a push reports is how that is observable from the outside.
    const abandoned = http.request({
        host: '127.0.0.1',
        port,
        method: 'GET',
        path: `/api/watch?version=${pushAlone.json.version}&hold=30`
    });
    abandoned.on('error', () => { });
    await new Promise(resolve => abandoned.end(resolve));
    await sleep(250);
    abandoned.destroy();
    await sleep(250);
    const afterAbandon = await postJson('/api/push', {});
    equal('a client that hung up while waiting is forgotten', afterAbandon.json.delivered, 0);
}

// ---------------------------------------------------------------------------
// Runner
// ---------------------------------------------------------------------------

async function main() {
    const temporary = fs.mkdtempSync(path.join(os.tmpdir(), 'smallpot-test-'));
    const workingDirectory = path.join(temporary, 'content');
    const statePath = path.join(temporary, 'state.json');

    // The suite writes to a working copy of its own, so whatever a designer has in server/content stays
    // exactly as it was. That is asserted at the end rather than assumed.
    const realContentDirectory = path.resolve(__dirname, 'content');
    const realContentBefore = fs.existsSync(realContentDirectory)
        ? fs.readdirSync(realContentDirectory).sort().join(',')
        : null;

    const app = server.createServer({
        contentDirectory: workingDirectory,
        statePath,
        quiet: true
    });

    await new Promise(resolve => app.server.listen(0, '127.0.0.1', resolve));
    port = app.server.address().port;
    console.log(`Small Pot server tests - port ${port}, working copy in ${workingDirectory}`);

    try {
        await run(workingDirectory, statePath);

        const realContentAfter = fs.existsSync(realContentDirectory)
            ? fs.readdirSync(realContentDirectory).sort().join(',')
            : null;
        check('the real content directory was left alone', realContentAfter === realContentBefore,
            `${realContentBefore} -> ${realContentAfter}`);
    } finally {
        await new Promise(resolve => app.server.close(resolve));
        fs.rmSync(temporary, { recursive: true, force: true });
    }

    console.log('');
    if (failures.length === 0) {
        console.log(`All ${passed} checks passed.`);
        process.exit(0);
    }
    console.log(`${passed} passed, ${failures.length} failed:`);
    for (const failure of failures) {
        console.log('  - ' + failure);
    }
    process.exit(1);
}

main().catch(error => {
    console.error('');
    console.error('The suite could not finish: ' + (error && error.stack ? error.stack : error));
    process.exit(1);
});
