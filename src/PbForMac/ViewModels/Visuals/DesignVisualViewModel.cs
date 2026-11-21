using CommunityToolkit.Mvvm.Input;
using PbForMac.Models;

namespace PbForMac.ViewModels.Visuals;

/// <summary>Фигура, текстовый блок или кнопка — оформление страницы без данных.</summary>
public sealed partial class DesignVisualViewModel : VisualViewModel
{
    public DesignVisualViewModel(VisualDefinition definition, ReportViewModel owner) : base(definition, owner)
    {
    }

    public override bool ShowsCategory => false;
    public override bool ShowsValues => false;
    public override bool ShowsAggregation => false;
    public override bool ShowsTopN => false;

    public override string AutoTitle => string.IsNullOrWhiteSpace(Text) ? KindLabel : Text;

    public override void OnModelChanged() => Refresh();

    protected override void RefreshCore()
    {
    }

    [RelayCommand]
    private void Invoke()
    {
        switch (Action.Value)
        {
            case DesignAction.GoToPage:
                Owner.GoToPage(ActionTarget);
                break;
            case DesignAction.ApplyBookmark:
                Owner.ApplyBookmarkByName(ActionTarget);
                break;
            case DesignAction.ResetSlicers:
                Owner.ResetSlicers();
                break;
        }
    }
}
