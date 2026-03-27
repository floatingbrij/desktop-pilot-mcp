const fs = require('fs');
const path = require('path');

const src = path.join(__dirname, '..', 'vscode-winapp-mcp', 'server');
const dest = path.join(__dirname, '..', 'server');

if (!fs.existsSync(src)) {
    console.error('Error: vscode-winapp-mcp/server/ not found. Build the .NET server first:');
    console.error('  cd src && dotnet publish -c Release -r win-x64 --self-contained -o ../vscode-winapp-mcp/server');
    process.exit(1);
}

// Clean destination
if (fs.existsSync(dest)) {
    fs.rmSync(dest, { recursive: true });
}

// Copy recursively
function copyDir(s, d) {
    fs.mkdirSync(d, { recursive: true });
    for (const entry of fs.readdirSync(s, { withFileTypes: true })) {
        const sp = path.join(s, entry.name);
        const dp = path.join(d, entry.name);
        if (entry.isDirectory()) {
            copyDir(sp, dp);
        } else {
            fs.copyFileSync(sp, dp);
        }
    }
}

copyDir(src, dest);
console.log('Server binaries copied to server/');
