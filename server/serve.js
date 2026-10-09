#!/usr/bin/env node
/*
 * Tiny static host for the game's data files.
 *
 * The game keeps all of its state on the device; this server only hands out content. That is all the
 * brief needs from a network: the ability to change a word or a number without rebuilding the app.
 *
 *   node server/serve.js [port]
 *
 * Endpoints:
 *   GET /                 a listing of what is being served
 *   GET /health           liveness probe, used by the game's reload button
 *   GET /<file>.json      one of the data files, never cached
 *
 * Every response is sent with CORS headers and Cache-Control: no-store so that an edit is visible on
 * the next request rather than the next reinstall.
 */

const http = require('http');
const fs = require('fs');
const path = require('path');
const os = require('os');
const crypto = require('crypto');

const port = Number(process.argv[2] || process.env.PORT || 8787);
const dataDirectory = path.resolve(__dirname, '..', 'data');

function send(response, status, body, contentType) {
    response.writeHead(status, {
        'Content-Type': contentType || 'text/plain; charset=utf-8',
        'Access-Control-Allow-Origin': '*',
        'Access-Control-Allow-Methods': 'GET, HEAD, OPTIONS',
        'Access-Control-Allow-Headers': '*',
        'Cache-Control': 'no-store, no-cache, must-revalidate',
        'Pragma': 'no-cache'
    });
    response.end(body);
}

function listFiles() {
    try {
        return fs.readdirSync(dataDirectory).filter(name => name.endsWith('.json')).sort();
    } catch (error) {
        return [];
    }
}

function localAddresses() {
    const addresses = [];
    const interfaces = os.networkInterfaces();
    for (const name of Object.keys(interfaces)) {
        for (const entry of interfaces[name] || []) {
            if (entry.family === 'IPv4' && !entry.internal) {
                addresses.push(entry.address);
            }
        }
    }
    return addresses;
}

const server = http.createServer((request, response) => {
    const started = Date.now();
    const url = new URL(request.url, `http://${request.headers.host || 'localhost'}`);
    const route = decodeURIComponent(url.pathname);
    const stamp = new Date().toISOString().substring(11, 19);

    if (request.method === 'OPTIONS') {
        send(response, 204, '');
        return;
    }

    if (request.method !== 'GET' && request.method !== 'HEAD') {
        send(response, 405, 'Only GET and HEAD are supported.\n');
        return;
    }

    if (route === '/health') {
        const body = JSON.stringify({ ok: true, files: listFiles(), time: new Date().toISOString() });
        send(response, 200, body, 'application/json; charset=utf-8');
        console.log(`${stamp} 200 /health`);
        return;
    }

    if (route === '/') {
        const body = [
            'Small Pot data server',
            '',
            'Serving: ' + dataDirectory,
            '',
            'Files:',
            ...listFiles().map(name => '  /' + name)
        ].join('\n') + '\n';
        send(response, 200, body);
        console.log(`${stamp} 200 /`);
        return;
    }

    // Resolve inside the data directory only, so a crafted path cannot walk out of it.
    const requested = path.resolve(dataDirectory, '.' + route);
    if (!requested.startsWith(dataDirectory + path.sep) || !requested.endsWith('.json')) {
        send(response, 403, 'Only the data files can be requested.\n');
        console.log(`${stamp} 403 ${route}`);
        return;
    }

    fs.readFile(requested, (error, content) => {
        if (error) {
            send(response, 404, `No such data file: ${route}\n`);
            console.log(`${stamp} 404 ${route}`);
            return;
        }

        // An ETag lets the game skip re-parsing a file it already has, and still notice an edit.
        const etag = '"' + crypto.createHash('sha1').update(content).digest('hex').substring(0, 16) + '"';
        if (request.headers['if-none-match'] === etag) {
            response.writeHead(304, { 'ETag': etag, 'Cache-Control': 'no-store' });
            response.end();
            console.log(`${stamp} 304 ${route}`);
            return;
        }

        response.setHeader('ETag', etag);
        send(response, 200, content, 'application/json; charset=utf-8');
        console.log(`${stamp} 200 ${route} (${content.length} bytes, ${Date.now() - started} ms)`);
    });
});

server.listen(port, () => {
    console.log(`Small Pot data server listening on port ${port}`);
    console.log(`Serving ${dataDirectory}`);
    console.log('');
    console.log('  On this machine : http://127.0.0.1:' + port);
    for (const address of localAddresses()) {
        console.log('  On the network  : http://' + address + ':' + port);
    }
    console.log('');
    console.log('  Over USB, with the phone attached, run: adb reverse tcp:' + port + ' tcp:' + port);
    console.log('  then use http://127.0.0.1:' + port + ' inside the app.');
    console.log('');
    console.log('Edit any file in data/ and reload from the game\'s settings panel.');
});
