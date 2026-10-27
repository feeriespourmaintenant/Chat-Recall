using Terraria.ModLoader;

namespace ChatRecall
{
	public class ChatRecall : Mod
	{
		public override void Load()
		{
			Logger.Info("Chat Recall build 5");
		}

		public override object Call(params object[] args) => ChatRecallApi.Call(args);
	}
}
