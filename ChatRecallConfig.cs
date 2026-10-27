using System.ComponentModel;
using Microsoft.Xna.Framework.Input;
using Terraria.ModLoader.Config;

namespace ChatRecall
{
	public class ChatRecallConfig : ModConfig
	{
		public override ConfigScope Mode => ConfigScope.ClientSide;

		[DefaultValue(100)]
		[Range(1, 10000)]
		public int HistorySize;

		[DefaultValue(Keys.Up)]
		public Keys PreviousKey;

		[DefaultValue(Keys.Down)]
		public Keys NextKey;

		[DefaultValue(Keys.Tab)]
		public Keys CompleteKey;

		[DefaultValue(true)]
		public bool ShowCommandHelp;

		[DefaultValue(false)]
		public bool AlwaysSuggestPlayers;

		[DefaultValue(false)]
		public bool KeepBetweenSessions;

		public override void OnChanged()
		{
			ChatRecallSystem.Trim(this);
		}
	}
}
