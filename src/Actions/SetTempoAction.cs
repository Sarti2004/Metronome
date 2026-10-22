using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;

namespace Metronome.Actions;

internal sealed class SetTempoAction(PluginIntegration integration) : IActionDefinition
{
	private const string TempoParameter = "tempo";

	public string Id => "set-tempo";

	public LocalizedText Name => Strings.Actions.SetTempo.Name();

	public LocalizedText Description => Strings.Actions.SetTempo.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.Number(TempoParameter,
			label: Strings.Actions.SetTempo.Tempo.Label(),
			min: MetronomeWidget.MinTempo,
			max: MetronomeWidget.MaxTempo,
			step: 1,
			defaultValue: MetronomeWidget.DefaultTempo,
			required: true),
	];

	public IActionExecutor CreateExecutor() => new Executor(integration);

	private sealed class Executor(PluginIntegration integration) : IActionExecutor
	{
		public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var label = Strings.Actions.SetTempo.Tempo.Label();
			if (context.Parameters.GetValueOrDefault(TempoParameter) is not double value || !double.IsFinite(value))
			{
				return Task.FromResult(ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					MacroDeckStrings.Validation.InvalidValue(label)));
			}

			var tempo = (int)Math.Round(value);
			if (tempo < MetronomeWidget.MinTempo)
			{
				return Task.FromResult(ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					MacroDeckStrings.Validation.AtLeast(label, MetronomeWidget.MinTempo)));
			}

			if (tempo > MetronomeWidget.MaxTempo)
			{
				return Task.FromResult(ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					MacroDeckStrings.Validation.AtMost(label, MetronomeWidget.MaxTempo)));
			}

			var widgets = integration.GetOpenWidgets();
			if (widgets.Count == 0)
			{
				return Task.FromResult(ActionResult.Failed(ActionErrorCodes.NotFound, Strings.Actions.NoOpenMetronome()));
			}

			foreach (var widget in widgets)
			{
				widget.SetManualTempo(tempo);
			}

			return ActionResult.SucceededTask;
		}
	}
}
