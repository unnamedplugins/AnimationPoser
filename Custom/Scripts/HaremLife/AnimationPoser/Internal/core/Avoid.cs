namespace HaremLife
{
	public partial class AnimationPoser : MVRScript
	{
		private class Avoid : AnimationObject
		{
			public Dictionary<string, State> myAvoidStates = new Dictionary<string, State>();
			public bool myIsPlaced = false;

			public Avoid(string name) : base(name) {
			}
		}
    }
}