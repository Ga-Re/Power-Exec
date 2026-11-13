using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using PowerExec.Core.Config;
using PowerExec.Desktop.Infrastructure;
using PowerExec.UI.ViewModels;

namespace PowerExec.Desktop.Views.Settings;

/// <summary>Einstellungen › Inhalte › Gruppen: Karten je Gruppe (rekursiv), Sortieren per Drag &amp; Drop.</summary>
public partial class GroupsPage : UserControl
{
    public GroupsPage() => InitializeComponent();

    private SettingsViewModel? Vm => (Window.GetWindow(this) as SettingsWindow)?.CurrentViewModel;

    // ---- Gruppen per Drag & Drop sortieren (unter Geschwistern) -----------------------

    private const string GroupDragFormat = "PowerExec.SettingsGroup";
    private Point _groupDragStart;
    private CategoryItemViewModel? _groupDragCandidate;
    private double _groupDragStartX;
    private DragFeedback? _dragFeedback;

    private void GroupCard_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Nicht aus Knöpfen, Auswahlfeldern, Textfeldern oder Schiebereglern heraus ziehen
        if (e.OriginalSource is DependencyObject src && !VisualTreeSearch.IsInside<ButtonBase>(src) && !VisualTreeSearch.IsInside<ComboBox>(src)
            && !VisualTreeSearch.IsInside<TextBox>(src) && !VisualTreeSearch.IsInside<Slider>(src) && !VisualTreeSearch.IsInside<Thumb>(src))
        {
            _groupDragStart = e.GetPosition(this);
            _groupDragCandidate = (sender as FrameworkElement)?.DataContext as CategoryItemViewModel;
        }
        else _groupDragCandidate = null;
    }

    private void GroupCard_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _groupDragCandidate == null || Vm is not { } vm) return;
        var d = e.GetPosition(this) - _groupDragStart;
        if (Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var item = _groupDragCandidate;
        _groupDragCandidate = null;
        _groupDragStartX = e.GetPosition(this).X;
        vm.BeginCategoryDrag(item);
        _dragFeedback = new DragFeedback(Window.GetWindow(this) ?? (FrameworkElement)this, vm.CategoryDragLabel, WpfColors.ToBrush(vm.CategoryDragColor), vm.GroupDragGhost, vm.GroupDragExplain);
        var source = (UIElement)sender;
        GiveFeedbackEventHandler follow = (_, _) => _dragFeedback?.Move();
        source.GiveFeedback += follow;
        try
        {
            DragDrop.DoDragDrop(source, new DataObject(GroupDragFormat, item), DragDropEffects.Move);
        }
        finally
        {
            source.GiveFeedback -= follow;
            _dragFeedback?.Dispose();
            _dragFeedback = null;
            vm.EndCategoryDrag();
        }
    }

    /// <summary>
    /// Bereich auf dem Kartenkopf wie im Panel: oberes Viertel = davor, Mitte = hinein, unteres Viertel = dahinter.
    /// Unterhalb des Kopfs (Details, Untergruppen) null = Ziel beibehalten.
    /// </summary>
    private static GroupDropZone? CardZone(Border card, DragEventArgs e, bool allowInto)
    {
        var head = (card.Child as Panel)?.Children.OfType<FrameworkElement>().FirstOrDefault();
        if (head == null || head.ActualHeight <= 0) return null;
        var y = e.GetPosition(head).Y;
        var h = head.ActualHeight;
        if (y > h) return null;
        // Wo „hinein“ nicht geht (feste Gruppe beteiligt): zwei Hälften statt eines schmalen Streifens für „davor“
        if (!allowInto) return y < h * 0.5 ? GroupDropZone.Before : GroupDropZone.After;
        return y < h * 0.28 ? GroupDropZone.Before
             : y > h * 0.72 ? GroupDropZone.After
             : GroupDropZone.Into;
    }

    /// <summary>Seitliches Ziehen um so viel wechselt die Ebene (wie die Einrückung der Untergruppen).</summary>
    private const double CategoryIndent = 24;

    private void ApplyCategoryPlan(GroupDropPlan? plan, DragEventArgs e)
    {
        e.Effects = plan is { IsValid: true } ? DragDropEffects.Move : DragDropEffects.None;
        _dragFeedback?.Update(plan);
        e.Handled = true;
    }

    private void GroupCard_DragOver(object sender, DragEventArgs e)
    {
        if (sender is not Border card || card.DataContext is not CategoryItemViewModel target || !e.Data.GetDataPresent(GroupDragFormat) || Vm is not { } vm) return;
        var offset = (int)Math.Round((e.GetPosition(this).X - _groupDragStartX) / CategoryIndent);
        var zone = CardZone(card, e, allowInto: !vm.CategoryDropIsTopLevelOnly(target));
        ApplyCategoryPlan(zone is { } z ? vm.PreviewCategoryDrop(target, z, offset) : vm.CurrentCategoryPlan, e);
    }

    /// <summary>Oberhalb der ersten Karte (Überschrift, Hinweise): an den Anfang.</summary>
    private void Page_DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(GroupDragFormat) || Vm is not { } vm) return;
        var above = e.GetPosition(CategoryList).Y < 0;
        ApplyCategoryPlan(above ? vm.PreviewCategoryDropAtStart() : vm.CurrentCategoryPlan, e);
    }

    private void Page_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(GroupDragFormat)) return;
        e.Handled = true;
        Vm?.CommitCategoryDrop();
    }

    private void GroupCard_DragLeave(object sender, DragEventArgs e)
    {
        // Lücke bleibt offen, bis ein anderes Ziel gefunden ist (sonst springt die Liste)
    }

    private void GroupCard_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(GroupDragFormat)) return;
        e.Handled = true;
        Vm?.CommitCategoryDrop();
    }

    /// <summary>Über der offenen Lücke: Ziel beibehalten.</summary>
    private void CategoryGap_DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(GroupDragFormat)) return;
        ApplyCategoryPlan(Vm?.CurrentCategoryPlan, e);
    }

    private void CategoryGap_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(GroupDragFormat)) return;
        e.Handled = true;
        Vm?.CommitCategoryDrop();
    }
}
