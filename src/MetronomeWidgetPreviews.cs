using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Previews;
using MacroDeck.Ui.Widgets;

namespace Metronome;

internal static class MetronomeWidgetPreviews
{
	[UiPreview("Default", Profile = UiPreviewProfiles.Widget)]
	public static UiElement Default() => Build(MetronomeWidget.DefaultTempo);

	[UiPreview("Slowest", Profile = UiPreviewProfiles.Widget)]
	public static UiElement Slowest() => Build(MetronomeWidget.MinTempo);

	[UiPreview("Fastest", Profile = UiPreviewProfiles.Widget)]
	public static UiElement Fastest() => Build(MetronomeWidget.MaxTempo);

	[UiPreview("Tapping", Profile = UiPreviewProfiles.Widget)]
	public static UiElement Tapping() => Build(96, tapping: true);

	[UiPreview("Playing", Profile = UiPreviewProfiles.Widget)]
	public static UiElement Playing() => Build(140, playing: true);

	private static UiElement Build(int tempo, bool tapping = false, bool playing = false)
		=> new MetronomeWidget(tempo, _ => { }, clickPlayer: null, ClickSounds.Default, beatImages: null, tapping, playing)
			.Build(null, UiWidgetSafeArea.For(UiWidgetSafeArea.DefaultCornerRadius));
}
