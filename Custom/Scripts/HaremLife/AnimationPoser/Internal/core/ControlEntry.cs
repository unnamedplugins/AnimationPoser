namespace HaremLife
{
	public partial class AnimationPoser : MVRScript
	{
		private class ControlEntry {
			public ControlTransform myTransform;
			public ControlCapture myControlCapture;

			public ControlEntry(ControlCapture controlCapture)
			{
				myControlCapture = controlCapture;
			}
		}
    }
}