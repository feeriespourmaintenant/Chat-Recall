using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework.Input;
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

			bool previous = Fire(cfg.PreviousKey, now);
			bool next = Fire(cfg.NextKey, now);
			bool complete = Fire(cfg.CompleteKey, now);

			if (previous)
				Recall(-1);
			else if (next)
				Recall(1);
			if (complete)
				Complete();

			// Hide the keys we use from the game, so the native chat scroll does not react to them.
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
				if (text.Length < 2 || text[0] != '/' || text.Contains(' '))
					return;
				matches = AllCommands().Where(c => c.StartsWith(text, StringComparison.OrdinalIgnoreCase)).ToList();
				if (matches.Count == 0)
					return;

				string result;
				cycleIndex = -1;
				if (matches.Count == 1) {
					result = matches[0] + " ";
				}
				else {
					string common = CommonPrefix(matches);
					if (common.Length > text.Length) {
						result = common;
					}
					else {
						cycleIndex = 0;
						result = matches[0];
					}
				}
				Main.chatText = lastCompletion = result;
			}
			else if (matches.Count > 1) {
				cycleIndex = (cycleIndex + 1) % matches.Count;
				Main.chatText = lastCompletion = matches[cycleIndex];
			}
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
		}
	}
}
