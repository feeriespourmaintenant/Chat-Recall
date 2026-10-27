# Chat Recall

A small client-side tModLoader mod for Terraria's chat.

- **Up / Down**: bring back the lines you sent before (messages and `/commands`), and go forward again. Down does nothing on the most recent line.
- **Tab**: complete a command. `/` alone does nothing, `/a` then Tab completes it, using the commands of the game and of every loaded mod. After a command, Tab also completes player names when the command's help mentions players (or always, with an option). Press Tab again to cycle through matches.
- **Settings**: keys, history size (1 to 10000, 100 by default), keep the history between sessions (off by default), always suggest player names (off by default), show the command help window (on by default). **Ctrl + Up / Down** scrolls the chat like the arrows did before (a reminder is shown in the chat bar). If you set other keys than the arrows, the game's native chat scrolling stays on the arrows.

## For mod authors: plug your commands into Tab (API v2)

No reference to this mod is needed, everything goes through `Mod.Call`. Register once, for example in `PostSetupContent`:

```csharp
if (ModLoader.TryGetMod("ChatRecall", out Mod chatRecall)) {
	// A type of your own: a function that receives what the player typed so far and returns proposals.
	chatRecall.Call("RegisterType", Mod, "zone", (Func<string, IEnumerable<string>>)(typed => new[] { "Tartarus", "Cocytus" }));

	// Arguments of /mycommand, in order. Each one is a type name, a fixed list of words, or a function.
	chatRecall.Call("RegisterCommand", Mod, "mycommand",
		"player",                         // built-in type: connected players
		new[] { "on", "off" },            // fixed words
		"zone");                          // your type (use "OtherMod/zone" to use another mod's type)
}
```

- `Call("ApiVersion")` returns `2` (version 1 calls keep working).
- Proposals are filtered by what the player typed, so a provider can return its full list.
- Commands you do not register keep the default behavior: player names when the command's help mentions players.
- A provider that throws is ignored and logged.

### Help window

While the player types `/mycommand `, a window next to the chat lists the parameters and examples. Describe your command once (texts can be a `string`, a `LocalizedText` or a `Func<string>`; labels starting with `[` are shown as optional, the others as required; the values of the argument being typed come from the types registered above):

```csharp
chatRecall.Call("DescribeCommand", Mod, "mycommand",
	Language.GetText("Mods.MyMod.Help.MyCommand.Description"),                       // description
	new object[] { "/mycommand on", "/mycommand off Alice" },                        // examples, clicking one fills the chat
	new object[] { "<mode>", "[player]" });                                          // one label per argument
```

A mod command you do not describe still gets a window with its own `Usage` and `Description`. Players can turn the window off in the settings.

## Build

Put this folder in `Documents/My Games/Terraria/tModLoader/ModSources/ChatRecall`, then use Workshop > Develop Mods > Build + Reload in tModLoader.

## License

MIT
