using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework.Input;
using Terraria.UI;
using Terraria;
using Terraria.ModLoader;

namespace ChatRecall
{
	public class ChatRecallSystem : ModSystem
	{
		static readonly string[] FallbackCommands = { "/playing", "/players", "/roll", "/emote", "/me", "/p", "/help", "/modlist" };

		internal static readonly List<string> History = new();

		static string HistoryPath => Path.Combine(Main.SavePath, "ChatRecall.history.txt");

		bool loaded;
		bool wasOpen;
		int index;
		string draft;
		KeyboardState prev;
		readonly Dictionary<Keys, int> held = new();

		// Tab completion state
		string lastCompletion;
		List<string> matches = new();
		int cycleIndex;

		// The game refreshes Main.keyState after PostUpdateInput and before reading the chat keys,
		// so the keys can only be hidden from the chat handler by running right before it.
		public override void Load()
		{
			if (!Main.dedServ)
				On_Main.DoUpdate_HandleChat += HandleChatHook;
		}

		public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
		{
			int chat = layers.FindIndex(l => l.Name == "Vanilla: Player Chat");
			if (chat < 0)
				return;
			layers.Insert(chat + 1, new LegacyGameInterfaceLayer("ChatRecall: Command help", () => {
				ChatRecallHelp.Draw(Main.spriteBatch);
				return true;
			}, InterfaceScaleType.UI));
		}

		static void HandleChatHook(On_Main.orig_DoUpdate_HandleChat orig)
		{
			ModContent.GetInstance<ChatRecallSystem>()?.ProcessInput();
			orig();
		}

		void ProcessInput()
		{
			if (!Main.hasFocus)
				return;

			KeyboardState now = Keyboard.GetState();

			if (!Main.drawingPlayerChat) {
				if (wasOpen)
					ResetChatState();
				prev = now;
				return;
			}

			ChatRecallConfig cfg = ModContent.GetInstance<ChatRecallConfig>();

			if (!wasOpen) {
				wasOpen = true;
				EnsureLoaded(cfg);
				index = History.Count;
				draft = null;
			}

			// Record the line before the game sends it and clears the box.
			if (now.IsKeyDown(Keys.Enter) && prev.IsKeyUp(Keys.Enter))
				Add(Main.chatText, cfg);

			// Ctrl + arrows is left to the game: it scrolls the chat like the arrows alone did before.
			bool ctrl = now.IsKeyDown(Keys.LeftControl) || now.IsKeyDown(Keys.RightControl);

			bool previous = !ctrl && Fire(cfg.PreviousKey, now);
			bool next = !ctrl && Fire(cfg.NextKey, now);
			bool complete = Fire(cfg.CompleteKey, now);

			if (previous)
				Recall(-1);
			else if (next)
				Recall(1);
			if (complete)
				Complete();

			// Hide the keys we use from the game, so the native chat scroll does not react to them.
			if (ctrl)
				Hide(now, cfg.CompleteKey);
			else
				Hide(now, cfg.PreviousKey, cfg.NextKey, cfg.CompleteKey);

			prev = now;
		}

		bool Fire(Keys key, KeyboardState now)
		{
			if (now.IsKeyUp(key)) {
				held.Remove(key);
				return false;
			}
			held.TryGetValue(key, out int frames);
			held[key] = frames + 1;
			return frames == 0 || (frames >= 24 && (frames - 24) % 3 == 0);
		}

		static void Hide(KeyboardState now, params Keys[] keys)
		{
			if (!keys.Any(now.IsKeyDown))
				return;
			Main.keyState = new KeyboardState(Main.keyState.GetPressedKeys().Where(k => !keys.Contains(k)).ToArray());
		}

		void ResetChatState()
		{
			wasOpen = false;
			held.Clear();
			draft = null;
			index = History.Count;
			lastCompletion = null;
			matches.Clear();
		}

		void Recall(int direction)
		{
			if (direction < 0) {
				if (index <= 0)
					return;
				if (index == History.Count)
					draft = Main.chatText;
				index--;
				Main.chatText = History[index];
			}
			else {
				if (index >= History.Count)
					return;
				index++;
				Main.chatText = index == History.Count ? draft ?? "" : History[index];
			}
		}

		void Complete()
		{
			string text = Main.chatText ?? "";

			if (text != lastCompletion) {
				if (TryBuildCompletion(text, out string result, out List<string> found, out int cycle)) {
					matches = found;
					cycleIndex = cycle;
					Main.chatText = lastCompletion = result;
				}
			}
			else if (matches.Count > 1) {
				cycleIndex = (cycleIndex + 1) % matches.Count;
				Main.chatText = lastCompletion = matches[cycleIndex];
			}
		}

		// What the next Tab would write, to show it in grey in the chat bar. Changes nothing.
		internal string PreviewCompletion(string text)
		{
			return text != lastCompletion && TryBuildCompletion(text, out string result, out _, out _) ? result : null;
		}

		static bool TryBuildCompletion(string text, out string result, out List<string> found, out int cycle)
		{
			result = null;
			found = new List<string>();
			cycle = -1;
			if (text.Length < 2 || text[0] != '/')
				return false;

			string suffix = "";
			int space = text.IndexOf(' ');
			if (space < 0) {
				found = AllCommands().Where(c => c.StartsWith(text, StringComparison.OrdinalIgnoreCase)).ToList();
				suffix = " ";
			}
			else {
				found = ChatRecallApi.IsRegistered(text.Substring(0, space)) ? ChatRecallApi.Complete(text, space) : PlayerMatches(text, space);
			}
			if (found.Count == 0)
				return false;

			if (found.Count == 1) {
				result = found[0] + suffix;
			}
			else {
				string common = CommonPrefix(found);
				if (common.Length > text.Length) {
					result = common;
				}
				else {
					cycle = 0;
					result = found[0];
				}
			}
			return true;
		}

		// Full chat lines with a connected player's name in place of the argument being typed.
		static List<string> PlayerMatches(string text, int space)
		{
			if (!AcceptsPlayers(text.Substring(0, space)))
				return new List<string>();

			List<string> names = ChatRecallApi.PlayerNames();

			// Player names can contain spaces: try everything after the command first, then only the last word.
			string head = text.Substring(0, space + 1);
			string tail = text.Substring(space + 1);
			List<string> found = names.Where(n => n.StartsWith(tail, StringComparison.OrdinalIgnoreCase)).Select(n => head + n).ToList();
			if (found.Count > 0)
				return found;

			int lastSpace = text.LastIndexOf(' ');
			head = text.Substring(0, lastSpace + 1);
			tail = text.Substring(lastSpace + 1);
			return names.Where(n => n.StartsWith(tail, StringComparison.OrdinalIgnoreCase)).Select(n => head + n).ToList();
		}

		// Mods only describe their arguments in free text: a command takes players if its help mentions them.
		static bool AcceptsPlayers(string commandWithSlash)
		{
			if (ModContent.GetInstance<ChatRecallConfig>().AlwaysSuggestPlayers)
				return true;

			foreach (Mod mod in ModLoader.Mods) {
				foreach (ModCommand command in mod.GetContent<ModCommand>()) {
					if (command.Type == CommandType.Console || !commandWithSlash.Equals("/" + command.Command, StringComparison.OrdinalIgnoreCase))
						continue;
					string help = (command.Usage ?? "") + " " + (command.Description ?? "");
					if (help.Contains("player", StringComparison.OrdinalIgnoreCase) || help.Contains("joueur", StringComparison.OrdinalIgnoreCase))
						return true;
				}
			}
			return false;
		}

		static string CommonPrefix(List<string> words)
		{
			string prefix = words[0];
			foreach (string word in words) {
				int i = 0;
				while (i < prefix.Length && i < word.Length && char.ToLowerInvariant(prefix[i]) == char.ToLowerInvariant(word[i]))
					i++;
				prefix = prefix.Substring(0, i);
			}
			return prefix;
		}

		// Every command declared by the game and by the loaded mods.
		static List<string> AllCommands()
		{
			var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (string name in FallbackCommands)
				set.Add(name);

			try {
				Type manager = typeof(Main).Assembly.GetType("Terraria.UI.Chat.ChatManager");
				const BindingFlags publicStatic = BindingFlags.Public | BindingFlags.Static;
				object processor = manager?.GetField("Commands", publicStatic)?.GetValue(null)
					?? manager?.GetProperty("Commands", publicStatic)?.GetValue(null);
				var commands = processor?.GetType().GetField("_localizedCommands", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(processor) as IDictionary;
				if (commands != null) {
					foreach (object key in commands.Keys) {
						string value = (key.GetType().GetProperty("Value")?.GetValue(key) as string)?.Trim();
						if (string.IsNullOrEmpty(value) || value.Contains(' '))
							continue;
						set.Add(value.StartsWith("/") ? value : "/" + value);
					}
				}
			}
			catch (Exception e) {
				ModContent.GetInstance<ChatRecall>().Logger.Warn("Could not read the game's chat commands: " + e.Message);
			}

			foreach (Mod mod in ModLoader.Mods) {
				foreach (ModCommand command in mod.GetContent<ModCommand>()) {
					if (command.Type == CommandType.Console || string.IsNullOrEmpty(command.Command))
						continue;
					set.Add("/" + command.Command);
				}
			}

			return set.ToList();
		}

		static void Add(string line, ChatRecallConfig cfg)
		{
			if (string.IsNullOrWhiteSpace(line))
				return;
			if (History.Count > 0 && History[^1] == line)
				return;
			History.Add(line);
			Trim(cfg);
			Save(cfg);
		}

		internal static void Trim(ChatRecallConfig cfg)
		{
			int excess = History.Count - Math.Max(1, cfg.HistorySize);
			if (excess > 0)
				History.RemoveRange(0, excess);
		}

		void EnsureLoaded(ChatRecallConfig cfg)
		{
			if (loaded)
				return;
			loaded = true;
			if (!cfg.KeepBetweenSessions)
				return;
			try {
				if (File.Exists(HistoryPath)) {
					History.InsertRange(0, File.ReadAllLines(HistoryPath).Where(l => !string.IsNullOrWhiteSpace(l)));
					Trim(cfg);
				}
			}
			catch (Exception e) {
				Mod.Logger.Warn("Could not read the chat history: " + e.Message);
			}
		}

		static void Save(ChatRecallConfig cfg)
		{
			if (!cfg.KeepBetweenSessions)
				return;
			try {
				File.WriteAllLines(HistoryPath, History);
			}
			catch (Exception e) {
				ModContent.GetInstance<ChatRecall>().Logger.Warn("Could not save the chat history: " + e.Message);
			}
		}

		public override void Unload()
		{
			if (!Main.dedServ)
				On_Main.DoUpdate_HandleChat -= HandleChatHook;
			History.Clear();
			ChatRecallApi.Clear();
		}
	}
}
