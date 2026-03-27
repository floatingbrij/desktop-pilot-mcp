#!/usr/bin/env node

const { spawn } = require('child_process');
const path = require('path');

const serverExe = path.join(__dirname, '..', 'server', 'WinAppMCP.exe');
const child = spawn(serverExe, process.argv.slice(2), {
    stdio: 'inherit',
    windowsHide: true
});

child.on('error', (err) => {
    if (err.code === 'ENOENT') {
        console.error('Error: WinAppMCP.exe not found. This package only works on Windows.');
        console.error('Expected path:', serverExe);
        process.exit(1);
    }
    console.error('Failed to start WinApp MCP server:', err.message);
    process.exit(1);
});

child.on('close', (code) => {
    process.exit(code ?? 0);
});
