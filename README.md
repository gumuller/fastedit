# FastEdit

A fast, lightweight text and hex editor for Windows, built with WPF (.NET 8), AvalonEdit, and Fluent Design (WPF UI).

![.NET 8](https://img.shields.io/badge/.NET-8.0-blue)
![WPF](https://img.shields.io/badge/WPF-Fluent%20Design-blue)
![License](https://img.shields.io/badge/license-MIT-green)
[![CI](https://github.com/gumuller/fastedit/actions/workflows/ci.yml/badge.svg)](https://github.com/gumuller/fastedit/actions/workflows/ci.yml)

## Features

### Core Editing
- **Syntax highlighting** for 20+ languages (C#, JavaScript, TypeScript, Python, Java, C/C++, Rust, Go, HTML, CSS, XML, JSON, YAML, TOML, INI, SQL, PowerShell, Shell/Bash, Dockerfile, Markdown)
- **Hex editor** with virtualized scrolling for large binary files
- **Tabbed editing** with full session restore (cursor position, scroll offset)
- **Find & Replace** (Ctrl+F / Ctrl+H) with match case, whole words, and regex
- **Find in Files** (Ctrl+Shift+F) — search across all files in the open folder
- **Go to Line** (Ctrl+G)
- **Auto-complete** (Ctrl+Space) — language keywords + document words
- **Command Palette** (Ctrl+Shift+P) — fuzzy-search all commands

### Developer Productivity
- **JSON/XML Pretty Print & Minify** — format or compact structured data instantly
- **Code Folding** — collapse/expand code blocks (brace-based, XML, and indent-based for Python)
- **Bracket/Brace Matching** — automatic highlighting of matching pairs
- **Occurrence Highlighting** — select a word to highlight all occurrences
- **Select Next/All Occurrences** (Ctrl+D / Ctrl+Shift+L) — jump through matches
- **Indent Guides** — visual indentation level lines
- **Bookmarks** — toggle (Ctrl+F2), navigate next/prev (F2 / Shift+F2)
- **Line Operations** — duplicate line (Ctrl+Shift+D), move line up/down (Alt+↑/↓)
- **Split Editor** (Ctrl+\\) — view the same file in two panes
- **Rectangular Selection** — Alt+drag for column editing
- **Undo/Redo** (Ctrl+Z / Ctrl+Y)

### Text Tools
- **Case Transformations** — UPPERCASE, lowercase, Title Case, Invert Case
- **Line Tools** — remove duplicates, sort ascending/descending, trim whitespace
- **Indentation Conversion** — tabs ↔ spaces
- **Encode/Decode** — Base64, URL encoding
- **Checksums** — MD5, SHA-1, SHA-256, SHA-512 (shown in status bar)
- **Macro Recording/Playback** — record editor actions and replay N times

### Debugging & File Analysis
- **Built-in Terminal** (Ctrl+\`) — run commands without leaving the editor
- **Log File Tailing** — auto-reload files when modified (View → Auto-Reload)
- **Side-by-Side File Compare** — visual diff with color-coded changes (View → Compare Files)
- **Line Filters** (Ctrl+L) — color-coded text/regex filters with include/exclude modes, show-only-filtered-lines toggle, save/load filter sets (inspired by TextAnalysisTool.NET)
- **Hex Search** (Ctrl+F in hex mode) — search by hex bytes (`FF 00 AB`) or quoted text (`"hello"`)
- **Git Branch Detection** — current branch shown in status bar
- **Hex/Text Mode Toggle** — switch between text and hex views for any file
- **Binary Detection** — automatically opens binary files in hex mode
- **Line Ending Detection & Conversion** — CRLF/LF/CR shown in status bar, convert via Edit menu
- **Encoding Auto-Detection** — BOM, UTF-8, UTF-16 LE/BE detected automatically; round-trip preserving encoding on save
- **Encoding Picker** — re-read files with different encodings (UTF-8, UTF-16, ASCII, ISO-8859-1, Windows-1252)

### UI & Theming
- **Windows 11 Fluent Design** with Mica backdrop
- **9 Built-in Themes** — Dark, Light, Nord, RetroGreen, Dracula, Monokai, One Dark, Solarized Dark, Solarized Light
- **Custom Themes** — drop JSON files in `%AppData%/FastEdit/Themes/`
- **Document Minimap** — scrollable code overview sidebar
- **Zoom** — Ctrl+Plus/Minus/0 or Ctrl+Scroll
- **Word Wrap & Whitespace** toggles
- **File Explorer Sidebar** with folder tree

### File Management
- **Drag & Drop** files and folders
- **Recent Files** menu (last 10)
- **Save As** (Ctrl+Shift+S)
- **Print** (Ctrl+P) — print current document
- **Session Restore** — reopens your tabs with cursor positions on restart
- **Named Sessions** — save/load multiple session profiles
- **Auto-Save & Crash Recovery** — periodic backup with recovery on unclean shutdown
- **Workspaces** — multi-root folder projects with `.fastedit-workspace` files

## Keyboard Shortcuts

| Action | Shortcut |
|---|---|
| New File | Ctrl+N |
| Open File | Ctrl+O |
| Save | Ctrl+S |
| Save As | Ctrl+Shift+S |
| Close Tab | Ctrl+W |
| Find | Ctrl+F |
| Replace | Ctrl+H |
| Find in Files | Ctrl+Shift+F |
| Go to Line | Ctrl+G |
| Command Palette | Ctrl+Shift+P |
| Auto-Complete | Ctrl+Space |
| Duplicate Line | Ctrl+Shift+D |
| Move Line Up/Down | Alt+↑/↓ |
| Toggle Bookmark | Ctrl+F2 |
| Next/Prev Bookmark | F2 / Shift+F2 |
| Split Editor | Ctrl+\\ |
| Line Filters | Ctrl+L |
| Toggle Terminal | Ctrl+\` |
| Zoom In/Out | Ctrl+Plus/Minus |
| Reset Zoom | Ctrl+0 |
| Print | Ctrl+P |
| Select Next Occurrence | Ctrl+D |
| Select All Occurrences | Ctrl+Shift+L |

## Installing (Signed Builds)

Releases are signed with a **self-signed certificate** (no cost, trusted once). On first install, Windows SmartScreen will show an "unknown publisher" warning unless you import the public certificate.

**One-time setup (recommended for regular users):**

1. Download `FastEdit-SelfSigned.cer` from the [latest release](https://github.com/gumuller/fastedit/releases/latest).
2. In PowerShell:
   ```powershell
   Import-Certificate -FilePath .\FastEdit-SelfSigned.cer `
     -CertStoreLocation Cert:\CurrentUser\TrustedPublisher
   Import-Certificate -FilePath .\FastEdit-SelfSigned.cer `
     -CertStoreLocation Cert:\CurrentUser\Root
   ```
3. After this, all FastEdit builds signed with this certificate run without SmartScreen or UAC warnings.

**Without the import:** click "More info" → "Run anyway" on the SmartScreen dialog. The signature still verifies file integrity — you can confirm the publisher is `FastEdit Self-Signed` in the file's Properties → Digital Signatures tab.

Verify the cert thumbprint of any downloaded `.cer` matches the one printed in the [release notes](https://github.com/gumuller/fastedit/releases/latest) before importing.

## Building

```bash
# Clone the repository
git clone https://github.com/gumuller/fastedit.git
cd fastedit

# Build
dotnet build

# Run
dotnet run --project src/FastEdit

# Run tests (349 tests)
dotnet test
```

## Requirements

- .NET 8.0 SDK
- Windows 10/11

## Project Structure

```
FastEdit.sln
├── src/
│   ├── FastEdit/              # WPF application
│   │   ├── Helpers/           # FormatHelper, CompletionHelper, GitHelper, etc.
│   │   ├── Infrastructure/    # CommandRegistry, BindingProxy, Converters
│   │   ├── Services/          # FileService, ThemeService, SettingsService
│   │   ├── SyntaxHighlighting/ # Custom .xshd definitions (YAML, Rust, Go, etc.)
│   │   ├── ViewModels/        # MVVM ViewModels
│   │   └── Views/             # MainWindow, EditorHost, Dialogs, Controls
│   ├── FastEdit.Core/         # Core engine (hex editor, binary detection)
│   └── FastEdit.Theming/      # Theme definitions and loader
└── tests/
    └── FastEdit.Tests/        # 349 unit tests
```

## Custom Themes

Create a JSON file in `%AppData%/FastEdit/Themes/` with this structure:

```json
{
  "Name": "MyTheme",
  "DisplayName": "My Custom Theme",
  "IsDark": true,
  "Colors": {
    "WindowBackground": "#1E1E1E",
    "WindowForeground": "#D4D4D4",
    "EditorBackground": "#1E1E1E",
    "EditorForeground": "#D4D4D4",
    "Accent": "#007ACC"
  },
  "SyntaxColors": {
    "Keyword": "#569CD6",
    "String": "#CE9178",
    "Comment": "#6A9955",
    "Number": "#B5CEA8",
    "Type": "#4EC9B0"
  }
}
```

## License

MIT
