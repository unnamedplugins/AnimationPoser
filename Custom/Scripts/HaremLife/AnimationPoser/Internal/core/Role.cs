namespace HaremLife
{
	public partial class AnimationPoser : MVRScript
    {
		private class Role : AnimationObject
		{
			public Atom myPerson;

			public Role(string name) : base(name) {
			}
		}
    }
}