using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Runtime;

namespace Metronome;

public sealed class ViewSession : IUiSession
{
	private readonly UiView _view;
	private readonly IDisposable? _owner;

	public ViewSession(UiView view, IDisposable? owner = null)
	{
		_view = view;
		_owner = owner;
		_view.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
		_view.HandlerFaulted += (_, fault)
			=> Faulted?.Invoke(this, new UiSessionFaultedEventArgs(fault.Exception.Message, fault.Exception));
	}

	public event EventHandler? Changed;

	public event EventHandler<UiSessionFaultedEventArgs>? Faulted;

	public UiTree BuildTree() => _view.Tree;

	public IReadOnlyList<UiPatch> DrainPatches() => _view.DrainPatches();

	public void Dispatch(UiEvent uiEvent) => _view.Dispatch(uiEvent);

	public void Dispatch(UiEvent uiEvent, IUiEventOrigin origin) => _view.Dispatch(uiEvent);

	public ValueTask DisposeAsync()
	{
		_owner?.Dispose();
		_view.Dispose();
		return ValueTask.CompletedTask;
	}
}
