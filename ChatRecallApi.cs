using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.ModLoader;

namespace ChatRecall
{
	// Other mods plug their commands into the Tab completion through Mod.Call, without referencing this mod.
	internal static class ChatRecallApi
	{
		internal const int ApiVersion = 2;

		internal class ArgSpec
		{
			public Func<string, IEnumerable<string>> Provider;
			public string TypeName;
			public string Owner;
		}

		static readonly Dictionary<string, Func<string, IEnumerable<string>>> Types = new();
		static readonly Dictionary<string, List<ArgSpec>> Commands = new(StringComparer.OrdinalIgnoreCase);
		static readonly Dictionary<string, CommandInfo> Infos = new(StringComparer.OrdinalIgnoreCase);

		// What the help window shows for a command. Texts can be a string, a LocalizedText or a Func<string>.
		internal class CommandInfo
		{
			public object Description;
			public List<object> Examples = new();
			public List<object> Labels = new();
		}

		internal static object Call(object[] args)
		{
			try {
				switch (args[0] as string) {
					case "ApiVersion":
						return ApiVersion;
					case "RegisterType":
						return RegisterType(OwnerName(args[1]), (string)args[2], (Func<string, IEnumerable<string>>)args[3]);
					case "RegisterCommand":
						return RegisterCommand(OwnerName(args[1]), (string)args[2], args.Skip(3).ToArray());
					case "DescribeCommand":
						return DescribeCommand((string)args[2], args[3], args.Length > 4 ? args[4] as IEnumerable<object> : null, args.Length > 5 ? args[5] as IEnumerable<object> : null);
				}
			}
			catch (Exception e) {
				ModContent.GetInstance<ChatRecall>().Logger.Warn("Chat Recall API call failed: " + e.Message);
				return false;
			}
			ModContent.GetInstance<ChatRecall>().Logger.Warn("Chat Recall API: unknown call " + args[0]);
			return null;
		}

		static string OwnerName(object caller) => caller is Mod mod ? mod.Name : (string)caller;

		static bool RegisterType(string owner, string name, Func<string, IEnumerable<string>> provider)
		{
			if (string.IsNullOrWhiteSpace(name) || name.Contains('/') || provider == null)
				return false;
			Types[owner + "/" + name] = provider;
			return true;
		}

		static bool RegisterCommand(string owner, string command, object[] arguments)
		{
			command = (command ?? "").TrimStart('/');
			if (command.Length == 0 || command.Contains(' '))
				return false;

			var specs = new List<ArgSpec>();
			foreach (object argument in arguments) {
				switch (argument) {
					case string typeName:
						specs.Add(new ArgSpec { TypeName = typeName, Owner = owner });
						break;
					case Func<string, IEnumerable<string>> provider:
						specs.Add(new ArgSpec { Provider = provider });
						break;
					case IEnumerable<string> words:
						string[] fixedWords = words.ToArray();
						specs.Add(new ArgSpec { Provider = _ => fixedWords });
						break;
					default:
						return false;
				}
			}
			Commands["/" + command] = specs;
			return true;
		}

		static bool DescribeCommand(string command, object description, IEnumerable<object> examples, IEnumerable<object> labels)
		{
			command = (command ?? "").TrimStart('/');
			if (command.Length == 0 || command.Contains(' '))
				return false;
			Infos["/" + command] = new CommandInfo {
				Description = description,
				Examples = examples?.ToList() ?? new List<object>(),
				Labels = labels?.ToList() ?? new List<object>()
			};
			return true;
		}

		internal static void Clear()
		{
			Types.Clear();
			Commands.Clear();
			Infos.Clear();
		}

		internal static CommandInfo GetInfo(string commandWithSlash) => Infos.TryGetValue(commandWithSlash, out CommandInfo info) ? info : null;

		internal static List<ArgSpec> GetSpecs(string commandWithSlash) => Commands.TryGetValue(commandWithSlash, out List<ArgSpec> specs) ? specs : null;

		internal static string ResolveText(object text)
		{
			try {
				return text switch {
					string s => s,
					Terraria.Localization.LocalizedText t => t.Value,
					Func<string> f => f(),
					_ => text?.ToString() ?? ""
				};
			}
			catch (Exception) {
				return "";
			}
		}

		internal static bool IsRegistered(string commandWithSlash) => Commands.ContainsKey(commandWithSlash);

		// Full chat lines with the argument being typed replaced by each proposal.
		internal static List<string> Complete(string text, int space)
		{
			var result = new List<string>();
			if (!Commands.TryGetValue(text.Substring(0, space), out List<ArgSpec> specs) || specs.Count == 0)
				return result;

			string[] parts = text.Substring(space + 1).Split(' ');
			int index = parts.Length - 1;
			if (index < specs.Count)
				result = Build(text, parts[index], specs[index]);

			// Names can contain spaces: the last argument may take every remaining word.
			if (result.Count == 0 && specs.Count - 1 < index) {
				int last = specs.Count - 1;
				result = Build(text, string.Join(" ", parts.Skip(last)), specs[last]);
			}
			return result;
		}

		static List<string> Build(string text, string typed, ArgSpec spec)
		{
			string head = text.Substring(0, text.Length - typed.Length);
			return Provide(spec, typed)
				.Where(name => !string.IsNullOrEmpty(name) && name.StartsWith(typed, StringComparison.OrdinalIgnoreCase))
				.Distinct()
				.Select(name => head + name)
				.ToList();
		}

		internal static IEnumerable<string> Provide(ArgSpec spec, string typed)
		{
			try {
				Func<string, IEnumerable<string>> provider = spec.Provider;
				if (provider == null && spec.TypeName != null) {
					if (spec.TypeName.Equals("player", StringComparison.OrdinalIgnoreCase))
						return PlayerNames();
					if (!Types.TryGetValue(spec.TypeName.Contains('/') ? spec.TypeName : spec.Owner + "/" + spec.TypeName, out provider))
						return Array.Empty<string>();
				}
				return provider?.Invoke(typed) ?? Array.Empty<string>();
			}
			catch (Exception e) {
				ModContent.GetInstance<ChatRecall>().Logger.Warn("A Chat Recall suggestion provider failed: " + e.Message);
				return Array.Empty<string>();
			}
		}

		internal static List<string> PlayerNames() =>
			Main.player.Where(p => p.active && !string.IsNullOrEmpty(p.name)).Select(p => p.name).Distinct().ToList();
	}
}
