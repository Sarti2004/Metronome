using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;

namespace Metronome.Actions;

internal sealed class StartStopAction(PluginIntegration integration) : IActionDefinition
{
	private const string CommandParameter = "command";
	private const string Toggle = "toggle";
	private const string Start = "start";
	private const string Stop = "stop";

	public string Id => "start-stop";

	public LocalizedText Name => Strings.Actions.StartStop.Name();

	public LocalizedText Description => Strings.Actions.StartStop.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.Choice(CommandParameter,
			[
				new ActionParameterOption { Value = Toggle, Label = Strings.Actions.StartStop.Commands.Toggle() },
				new ActionParameterOption { Value = Start, Label = Strings.Actions.StartStop.Commands.Start() },
				new ActionParameterOption { Value = Stop, Label = Strings.Actions.StartStop.Commands.Stop() },
			],
			label: Strings.Actions.StartStop.Command.Label(),
			defaultValue: Toggle,
			required: true),
	];

	public IActionExecutor CreateExecutor() => new Executor(integration);

	private sealed class Executor(PluginIntegration integration) : IActionExecutor
	{
		public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var command = context.Parameters.GetValueOrDefault(CommandParameter) as string;
			if (command is not (Toggle or Start or Stop))
			{
				return Task.FromResult(ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					MacroDeckStrings.Validation.InvalidValue(Strings.Actions.StartStop.Command.Label())));
			}

			var widgets = integration.GetOpenWidgets();
			if (widgets.Count == 0)
			{
				return Task.FromResult(ActionResult.Failed(ActionErrorCodes.NotFound, Strings.Actions.NoOpenMetronome()));
			}

			// Toggling several metronomes moves them all to one state rather than flipping each, so a
			// mixed set ends up silent instead of swapping which ones play.
			var playing = command switch
			{
				Start => true,
				Stop => false,
				_ => !widgets.Any(widget => widget.IsPlaying),
			};

			foreach (var widget in widgets)
			{
				widget.SetPlaying(playing);
			}

			return ActionResult.SucceededTask;
		}
	}
}
