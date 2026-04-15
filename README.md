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
- **Indent Guides** — visual indentation level lines
- **Bookmarks** — toggle (Ctrl+F2), navigate next/prev (F2 / Shift+F2)
- **Line Operations** — duplicate line (Ctrl+Shift+D), move line up/down (Alt+↑/↓)
- **Split Editor** (Ctrl+\\) — view the same file in two panes
- **Rectangular Selection** — Alt+drag for column editing
- **Undo/Redo** (Ctrl+Z / Ctrl+Y)

### Debugging & File Analysis
- **Built-in Terminal** (Ctrl+\`) — run commands without leaving the editor
- **Log File Tailing** — auto-reload files when modified (View → Auto-Reload)
- **Side-by-Side File Compare** — visual diff with color-coded changes (View → Compare Files)
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
- **Session Restore** — reopens your tabs with cursor positions on restart

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
| Toggle Terminal | Ctrl+\` |
| Zoom In/Out | Ctrl+Plus/Minus |
| Reset Zoom | Ctrl+0 |

## Building

```bash
# Clone the repository
git clone https://github.com/gumuller/fastedit.git
cd fastedit

# Build
dotnet build

# Run
dotnet run --project src/FastEdit

# Run tests (281 tests)
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
    └── FastEdit.Tests/        # 281 unit tests
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
