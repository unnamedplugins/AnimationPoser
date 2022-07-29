namespace HaremLife
{
	public partial class AnimationPoser : MVRScript
	{
		private class Message : AnimationObject
		{
			public State myTargetState;
			public String myMessageString;
			public Dictionary<string, State> mySourceStates = new Dictionary<string, State>();

			public Message(string name) : base(name) {
			}
		}
    }
}