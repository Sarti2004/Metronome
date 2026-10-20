using System.Text.Json;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Config.Options;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Runtime;

namespace Metronome;

public static class MetronomeConfigView
{
	public static UiElement Build(JsonElement data, ClickPlayer clickPlayer)
	{
		var sound = new UiState<string>(ClickSounds.Read(data));
		return new UiWidgetConfiguration
		{
			Key = "root",
			Properties = new UiWidgetProperties
			{
				Key = "properties",
				Children =
				[
					new UiChoiceInput
					{
						Key = ClickSounds.DataKey,
						Label = UiText.Of(Strings.Widgets.Metronome.Sound.Label()),
						Description = UiText.Of(Strings.Widgets.Metronome.Sound.Description()),
						Options = UiValue.Of<IReadOnlyList<UiOption>>(
							[.. ClickSounds.All.Select(option => UiOption.Of(option.Id, option.Label()))]),
						Binding = Bind.Custom(() => sound.Value, value =>
						{
							sound.Value = value;
							_ = clickPlayer.AuditionAsync(value);
						}),
					},
				],
			},
		};
	}
}
