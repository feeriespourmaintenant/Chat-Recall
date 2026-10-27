using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.Localization;
using Terraria.UI.Chat;
using Terraria.ModLoader;

namespace ChatRecall
{
	// Window to the right of the chat that lists the parameters and examples of the command being typed.
	internal static class ChatRecallHelp
	{
		const int Width = 340;
		const int Margin = 14;
		const int MaxValues = 6;

		static readonly Color Gold = new(255, 214, 80);
		static readonly Color Text = new(225, 230, 250);
		static readonly Color Heading = new(160, 172, 225);
		static readonly Color Required = new(120, 230, 255);
		static readonly Color Optional = new(190, 190, 205);
		static readonly Color Dim = new(200, 205, 230);
		static readonly Color Highlight = new(255, 240, 150);

		static readonly List<(Rectangle area, string line)> exampleAreas = new();

		static string T(string key) => Language.GetTextValue("Mods.ChatRecall.Help." + key);

		internal static void Draw(SpriteBatch spriteBatch)
		{
			if (!Main.drawingPlayerChat)
				return;

			string typed = Main.chatText ?? "";
			if (typed.Length == 0 || typed[0] != '/') {
				// The key reminder is only there until a command is being typed.
				if (ModContent.GetInstance<ChatRecallConfig>().ShowKeyHints)
					DrawKeyHints(spriteBatch, typed);
				return;
			}
			if (typed.Length < 2)
				return;
			int space = typed.IndexOf(' ');

			// Recomputed when the text or the language changes, and every half second so changing lists (players) stay fresh.
			string key = typed + "\n" + Language.ActiveCulture.Name + "\n" + Main.GameUpdateCount / 30;
			if (key != cachedText) {
				cachedText = key;
				cachedGhost = ModContent.GetInstance<ChatRecallSystem>().PreviewCompletion(typed);
				if (space >= 0)
					Compute(typed, space);
				else
					cachedValid = false;
			}

			DrawGhost(spriteBatch, typed);
			if (space < 0 || !cachedValid || !ModContent.GetInstance<ChatRecallConfig>().ShowCommandHelp)
				return;

			string command = cachedCommand, description = cachedDescription;
			List<string> labels = cachedLabels, examples = cachedExamples, values = cachedValues;
			int argCount = cachedArgCount, current = cachedCurrent;
			string typedArgument = cachedArgument;

			float height = Layout(null, 0, 0, command, description, labels, argCount, current, values, typedArgument, examples);
			// Attached to the end of the typed text, like a bubble coming out of the chat bar.
			int x = Math.Clamp((int)TextEnd(typed) + 40, 360, Math.Max(360, Main.screenWidth - Width - 10));
			int y = Math.Max(10, Main.screenHeight - 6 - (int)height);
			var panel = new Rectangle(x, y, Width, (int)height);

			Utils.DrawInvBG(spriteBatch, panel);
			DrawPointer(spriteBatch, x, Main.screenHeight - 21);
			exampleAreas.Clear();
			Layout(spriteBatch, x + Margin, y + 10, command, description, labels, argCount, current, values, typedArgument, examples);

			if (panel.Contains(Main.mouseX, Main.mouseY))
				Main.LocalPlayer.mouseInterface = true;

			if (Main.mouseLeft && Main.mouseLeftRelease) {
				foreach ((Rectangle area, string line) in exampleAreas) {
					if (area.Contains(Main.mouseX, Main.mouseY)) {
						Main.chatText = line;
						Main.mouseLeftRelease = false;
						SoundEngine.PlaySound(SoundID.MenuTick);
						break;
					}
				}
			}
		}

		// Texts and suggestions only change when the chat text does, so mods' providers are not called every frame.
		static string cachedGhost;
		static string cachedText, cachedCommand, cachedDescription, cachedArgument;
		static bool cachedValid;
		static List<string> cachedLabels = new(), cachedExamples = new(), cachedValues = new();
		static int cachedArgCount, cachedCurrent;

		static void Compute(string typed, int space)
		{
			string command = typed.Substring(0, space);
			ChatRecallApi.CommandInfo info = ChatRecallApi.GetInfo(command);
			List<ChatRecallApi.ArgSpec> specs = ChatRecallApi.GetSpecs(command);
			string fallback = info == null ? ModCommandHelp(command) : null;
			if (info == null && specs == null && fallback == null)
				{ cachedValid = false; return; }

			string description = info != null ? ChatRecallApi.ResolveText(info.Description) : fallback;
			List<string> labels = info?.Labels.Select(ChatRecallApi.ResolveText).ToList() ?? new List<string>();
			List<string> examples = info?.Examples.Select(ChatRecallApi.ResolveText).Where(e => e.Length > 0).ToList() ?? new List<string>();

			string[] parts = typed.Substring(space + 1).Split(' ');
			int argCount = Math.Max(labels.Count, specs?.Count ?? 0);
			int current = argCount == 0 ? -1 : Math.Min(parts.Length - 1, argCount - 1);
			string typedArgument = current < 0 ? "" : string.Join(" ", parts.Skip(current));
			List<string> values = new();
			if (specs != null && current >= 0 && current < specs.Count)
				values = ChatRecallApi.Provide(specs[current], typedArgument)
					.Where(v => !string.IsNullOrEmpty(v) && v.StartsWith(typedArgument, StringComparison.OrdinalIgnoreCase))
					.Distinct().ToList();

			cachedValid = true;
			cachedCommand = command;
			cachedDescription = description;
			cachedLabels = labels;
			cachedExamples = examples;
			cachedValues = values;
			cachedArgCount = argCount;
			cachedCurrent = current;
			cachedArgument = typedArgument;
		}

		static readonly Color KeyColor = new(170, 178, 215);
		static readonly Color KeyHot = new(255, 214, 80);

		// Reminder of the keys, inside the chat bar on its right side. The Ctrl + arrows one is gold on purpose.
		static void DrawKeyHints(SpriteBatch spriteBatch, string typed)
		{
			ChatRecallConfig cfg = ModContent.GetInstance<ChatRecallConfig>();
			int right = (Main.screenWidth > 800 ? 78 + Main.screenWidth - 300 : 78 + TextureAssets.TextBack.Width()) - 14;
			int centerY = Main.screenHeight - 21;
			int width = KeyHints(null, 0, centerY, cfg);
			int left = right - width;
			if (left < TextEnd(typed) + 24)
				return;
			KeyHints(spriteBatch, left, centerY, cfg);
		}

		static int KeyHints(SpriteBatch sb, int x0, int cy, ChatRecallConfig cfg)
		{
			int x = x0;
			bool arrows = cfg.PreviousKey == Keys.Up && cfg.NextKey == Keys.Down;
			if (arrows) {
				x += Key(sb, x, cy, null, -1, KeyColor) + 3;
				x += Key(sb, x, cy, null, 1, KeyColor) + 3;
			}
			else {
				x += Key(sb, x, cy, cfg.PreviousKey.ToString(), 0, KeyColor) + 3;
				x += Key(sb, x, cy, cfg.NextKey.ToString(), 0, KeyColor) + 3;
			}
			x += Label(sb, x, cy, T("KeyHistory"), Dim) + 14;
			x += Key(sb, x, cy, cfg.CompleteKey.ToString(), 0, KeyColor) + 3;
			x += Label(sb, x, cy, T("KeyComplete"), Dim) + 14;
			x += Key(sb, x, cy, "Ctrl", 0, KeyHot) + 3;
			x += Label(sb, x, cy, "+", KeyHot) + 4;
			x += Key(sb, x, cy, null, -1, KeyHot) + 3;
			x += Key(sb, x, cy, null, 1, KeyHot) + 3;
			x += Label(sb, x, cy, T("KeyScroll"), new Color(255, 230, 150));
			return x - x0;
		}

		static int Label(SpriteBatch sb, int x, int cy, string text, Color color)
		{
			Vector2 size = FontAssets.MouseText.Value.MeasureString(text) * 0.85f;
			if (sb != null)
				Utils.DrawBorderString(sb, text, new Vector2(x + 4, cy - size.Y / 2 + 2), color, 0.85f);
			return (int)size.X + 4;
		}

		// A small key cap: a label, or an arrow when the label is null (direction -1 up, 1 down).
		static int Key(SpriteBatch sb, int x, int cy, string label, int direction, Color color)
		{
			var font = FontAssets.MouseText.Value;
			int width = label == null ? 28 : (int)(font.MeasureString(label).X * 0.8f) + 14;
			if (sb == null)
				return width;

			var pixel = TextureAssets.MagicPixel.Value;
			var cap = new Rectangle(x, cy - 11, width, 22);
			sb.Draw(pixel, cap, color);
			sb.Draw(pixel, new Rectangle(cap.X + 2, cap.Y + 2, cap.Width - 4, cap.Height - 4), new Color(30, 36, 70));
			if (label != null) {
				Utils.DrawBorderString(sb, label, new Vector2(x + 7, cy - 9), color, 0.8f);
			}
			else {
				int cx = x + width / 2;
				for (int i = 0; i < 6; i++) {
					int row = direction < 0 ? cy - 6 + i : cy + 5 - i;
					sb.Draw(pixel, new Rectangle(cx - i, row, 2 * i + 1, 1), color);
				}
			}
			return width;
		}

		static float TextEnd(string typed)
		{
			var snippets = ChatManager.ParseMessage(typed, Color.White).ToArray();
			return 88 + ChatManager.GetStringSize(FontAssets.MouseText.Value, snippets, Vector2.One).X;
		}

		// Grey rest of the line that Tab would write, drawn in the chat bar after the typed text.
		static void DrawGhost(SpriteBatch spriteBatch, string typed)
		{
			if (cachedGhost == null || cachedGhost.Length <= typed.Length || !cachedGhost.StartsWith(typed, StringComparison.OrdinalIgnoreCase))
				return;
			string ghost = cachedGhost.Substring(typed.Length);
			Utils.DrawBorderString(spriteBatch, ghost, new Vector2(TextEnd(typed) + 6, Main.screenHeight - 30), new Color(150, 152, 175) * 0.7f);
		}

		// Small triangle on the left edge of the window, pointing at the chat bar.
		static void DrawPointer(SpriteBatch spriteBatch, int x, int centerY)
		{
			var pixel = TextureAssets.MagicPixel.Value;
			for (int dy = -10; dy <= 10; dy++) {
				int width = 11 - Math.Abs(dy);
				spriteBatch.Draw(pixel, new Rectangle(x - width - 2, centerY + dy, width + 4, 1), new Color(10, 14, 34));
				spriteBatch.Draw(pixel, new Rectangle(x - width, centerY + dy, width + 2, 1), new Color(63, 65, 151) * 0.9f);
			}
		}

		// With a null sprite batch it only measures; returns the height of the whole window.
		static float Layout(SpriteBatch sb, float x, float top, string command, string description, List<string> labels,
			int argCount, int current, List<string> values, string typedArgument, List<string> examples)
		{
			var font = FontAssets.MouseText.Value;
			float inner = Width - 2 * Margin;
			float y = top;

			void Line(string text, float lx, Color color, float scale = 1f)
			{
				if (sb != null)
					Utils.DrawBorderString(sb, text, new Vector2(lx, y), color, scale);
			}

			Line(command, x, Gold, 1.15f);
			y += 30;

			if (!string.IsNullOrWhiteSpace(description)) {
				string[] lines = Utils.WordwrapString(description, font, (int)inner, 4, out int count);
				for (int i = 0; i <= count && i < lines.Length; i++) {
					if (string.IsNullOrEmpty(lines[i]))
						continue;
					Line(lines[i], x, Text, 0.9f);
					y += 22;
				}
			}

			if (argCount > 0) {
				y += 6;
				Line(T("Parameters"), x, Heading, 0.8f);
				y += 22;
				for (int i = 0; i < argCount; i++) {
					string label = i < labels.Count ? labels[i] : "<" + (i + 1) + ">";
					bool optional = label.StartsWith("[");
					Line(label, x, optional ? Optional : Required);
					float w = font.MeasureString(label + "  ").X;
					Line(optional ? T("Optional") : T("Required"), x + w, Dim, 0.8f);
					y += 26;

					if (i == current) {
						foreach (string value in values.Take(MaxValues)) {
							bool first = value == values[0];
							if (sb != null && first)
								sb.Draw(TextureAssets.MagicPixel.Value, new Rectangle((int)x - 4, (int)y - 1, (int)inner + 8, 24), new Color(86, 106, 196) * 0.9f);
							Line(value, x + 10, first ? Highlight : Text, 0.95f);
							y += 24;
						}
						if (values.Count > MaxValues) {
							Line("...", x + 10, Dim, 0.9f);
							y += 20;
						}
					}
				}
			}

			if (examples.Count > 0) {
				y += 8;
				Line(T("Examples"), x, Heading, 0.8f);
				y += 22;
				foreach (string example in examples) {
					var area = new Rectangle((int)x - 4, (int)y - 1, (int)inner + 8, 24);
					bool hover = sb != null && area.Contains(Main.mouseX, Main.mouseY);
					if (sb != null)
						exampleAreas.Add((area, example));
					Line(example, x + 4, hover ? Highlight : Text, 0.95f);
					y += 24;
				}
			}

			return y - top + 20;
		}

		// A mod command that is not registered: its own help text, if it has one.
		static string ModCommandHelp(string commandWithSlash)
		{
			foreach (Mod mod in ModLoader.Mods) {
				foreach (ModCommand command in mod.GetContent<ModCommand>()) {
					if (command.Type == CommandType.Console || !commandWithSlash.Equals("/" + command.Command, StringComparison.OrdinalIgnoreCase))
						continue;
					string help = string.Join("\n", new[] { command.Usage, command.Description }.Where(h => !string.IsNullOrWhiteSpace(h)));
					return help.Length > 0 ? help : null;
				}
			}
			return null;
		}
	}
}
