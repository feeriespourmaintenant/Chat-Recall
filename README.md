# Chat Recall

A small client-side tModLoader mod for Terraria's chat.

- **Up / Down**: bring back the lines you sent before (messages and `/commands`), and go forward again. Down does nothing on the most recent line.
- **Tab**: complete a command. `/` alone does nothing, `/a` then Tab completes it, using the commands of the game and of every loaded mod. Press Tab again to cycle through matches.
- **Settings**: keys, history size (1 to 10000, 100 by default), keep the history between sessions (off by default). If you set other keys than the arrows, the game's native chat scrolling stays on the arrows.

## Build

Put this folder in `Documents/My Games/Terraria/tModLoader/ModSources/ChatRecall`, then use Workshop > Develop Mods > Build + Reload in tModLoader.

## License

MIT
