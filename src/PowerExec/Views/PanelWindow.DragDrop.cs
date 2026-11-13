using PowerExec.Core.Localization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PowerExec.Core.Config;
using PowerExec.Core.Layout;
using PowerExec.Desktop.Infrastructure;
using PowerExec.Desktop.Interop;
using PowerExec.UI.Services;
using PowerExec.Desktop.Services;
using PowerExec.UI.ViewModels;

namespace PowerExec.Desktop.Views;

/// <content>Drag &amp; Drop im Panel: Reihenfolge der Einträge, Gruppen verschieben und verschachteln, Dateien aus dem Explorer.</content>
public partial class PanelWindow
{
    // ---- Drag & Drop: Reihenfolge innerhalb einer Gruppe ----------------------

    private const string DragFormat = "PowerExec.Item";
    private Point _dragStart;
    private ItemViewModel? _dragCandidate;

    private void Row_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Strg+Klick: zur Mehrfachauswahl hinzufügen bzw. entfernen (statt aufklappen oder starten)
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && (sender as FrameworkElement)?.DataContext is ItemViewModel checkedItem)
        {
            Vm.ToggleChecked(checkedItem);
            e.Handled = true;
            return;
        }
        _dragStart = e.GetPosition(this);
        _dragCandidate = (sender as FrameworkElement)?.DataContext as ItemViewModel;
    }

    private void Row_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragCandidate == null || Vm.IsSearching) return;
        var pos = e.GetPosition(this);
        if (Math.Abs(pos.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance * 2) return;
        var item = _dragCandidate;
        _dragCandidate = null;
        Mouse.Capture(null);
        DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(DragFormat, item), DragDropEffects.Move);
    }

    /// <summary>Einfügen hinter dem Ziel: in der Liste untere Hälfte, im Raster rechte Hälfte.</summary>
    private bool InsertAfter(FrameworkElement row, DragEventArgs e) => Vm.IsGrid
        ? e.GetPosition(row).X > row.ActualWidth / 2
        : e.GetPosition(row).Y > row.ActualHeight / 2;

    private Thickness InsertMarker(FrameworkElement row, DragEventArgs e) =>
        (row.DataContext is ItemViewModel item && GroupOf(item) is { } g ? g.IsGrid : Vm.IsGrid)
        ? (InsertAfter(row, e) ? new Thickness(0, 0, 2, 0) : new Thickness(2, 0, 0, 0))
        : (InsertAfter(row, e) ? new Thickness(0, 0, 0, 2) : new Thickness(0, 2, 0, 0));

    /// <summary>Einfügemarke (Akzentlinie) setzen bzw. entfernen – entfernt, gelten wieder Rand und Hover der Vorlage.</summary>
    private void SetInsertMarker(Border row, Thickness? marker)
    {
        if (marker is { } t)
        {
            row.BorderThickness = t;
            row.SetResourceReference(Border.BorderBrushProperty, "Accent");
        }
        else
        {
            row.ClearValue(Border.BorderThicknessProperty);
            row.ClearValue(Border.BorderBrushProperty);
        }
    }

    private GroupViewModel? GroupOf(ItemViewModel item) => Vm.AllGroups.FirstOrDefault(g => g.Items.Contains(item));

    private void ClearSelection_Click(object sender, RoutedEventArgs e) => Vm.ClearSelection();

    // ---- Drag & Drop: Gruppen verschieben und verschachteln ---------------------

    private const string GroupDragFormat = "PowerExec.Group";
    private Point _groupDragStart;
    private GroupViewModel? _groupDragCandidate;

    private void GroupHeader_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _groupDragStart = e.GetPosition(this);
        _groupDragCandidate = (sender as FrameworkElement)?.DataContext is GroupViewModel { CanDrag: true } g ? g : null;
    }

    private void GroupHeader_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _groupDragCandidate == null || Vm.IsSearching) return;
        var pos = e.GetPosition(this);
        if (Math.Abs(pos.X - _groupDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _groupDragStart.Y) < SystemParameters.MinimumVerticalDragDistance * 2) return;
        var group = _groupDragCandidate;
        _groupDragCandidate = null;
        Mouse.Capture(null);
        _groupDragStartX = e.GetPosition(this).X;
        Vm.BeginGroupDrag(group);
        var settings = _app.Settings;
        _dragFeedback = new DragFeedback(this, Vm.DragLabel, WpfColors.ToBrush(Vm.DragColor), settings.GroupDragGhost, settings.GroupDragExplain);
        var source = (UIElement)sender;
        GiveFeedbackEventHandler follow = (_, _) => _dragFeedback?.Move();
        source.GiveFeedback += follow;
        try
        {
            DragDrop.DoDragDrop(source, new DataObject(GroupDragFormat, group), DragDropEffects.Move);
        }
        finally
        {
            source.GiveFeedback -= follow;
            _dragFeedback?.Dispose();
            _dragFeedback = null;
            Vm.EndGroupDrag(); // abgebrochen oder außerhalb losgelassen
        }
    }

    private double _groupDragStartX;
    private DragFeedback? _dragFeedback;

    /// <summary>Einrückung je Ebene im Panel (Rand der Untergruppen-Liste): seitliches Ziehen um so viel wechselt die Ebene.</summary>
    private const double GroupIndent = 14;

    /// <summary>Oberes Viertel = davor, unteres Viertel = dahinter, Mitte = hinein (Untergruppe).</summary>
    private static Core.Config.GroupDropZone GroupZone(FrameworkElement header, DragEventArgs e)
    {
        var y = e.GetPosition(header).Y;
        var h = Math.Max(1, header.ActualHeight);
        return y < h * 0.28 ? Core.Config.GroupDropZone.Before
             : y > h * 0.72 ? Core.Config.GroupDropZone.After
             : Core.Config.GroupDropZone.Into;
    }

    private int LevelOffset(DragEventArgs e) => (int)Math.Round((e.GetPosition(this).X - _groupDragStartX) / GroupIndent);

    /// <summary>Effekt und Schild für den aktuellen Plan (bleibt über Lücke und Gruppeninhalt stehen).</summary>
    private void ApplyGroupPlan(Core.Config.GroupDropPlan? plan, DragEventArgs e)
    {
        e.Effects = plan is { IsValid: true } ? DragDropEffects.Move : DragDropEffects.None;
        _dragFeedback?.Update(plan);
        e.Handled = true;
    }

    private void GroupHeader_DragOver(object sender, DragEventArgs e)
    {
        if (sender is not Border header || !e.Data.GetDataPresent(GroupDragFormat)) return; // Einträge/Dateien: übernimmt die Gruppe
        if (header.DataContext is not GroupViewModel target) return;
        ApplyGroupPlan(Vm.PreviewGroupDrop(target, GroupZone(header, e), LevelOffset(e)), e);
    }

    private void GroupHeader_DragLeave(object sender, DragEventArgs e)
    {
        // Lücke bleibt offen, bis ein anderes Ziel gefunden ist (sonst springt die Liste)
    }

    private void GroupHeader_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(GroupDragFormat)) return;
        e.Handled = true;
        Vm.CommitGroupDrop();
    }

    /// <summary>Über der offenen Lücke: Ziel beibehalten.</summary>
    private void DropGap_DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(GroupDragFormat)) return;
        ApplyGroupPlan(Vm.CurrentGroupDropPlan, e);
    }

    private void DropGap_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(GroupDragFormat)) return;
        e.Handled = true;
        Vm.CommitGroupDrop();
    }

    private void Row_DragOver(object sender, DragEventArgs e)
    {
        if (sender is not Border row) return;
        if (e.Data.GetDataPresent(SettingsWindow.EntryDragFormat) && row.DataContext is ItemViewModel over && GroupOf(over) is { AcceptsItems: true } overGroup)
        {
            e.Effects = DragDropEffects.Move;
            SetInsertMarker(row, overGroup.Kind != "fav" ? InsertMarker(row, e) : null);
            e.Handled = true;
            return;
        }
        if (!e.Data.GetDataPresent(DragFormat)) return; // Dateien aus dem Explorer: übernimmt die Gruppe
        var source = (ItemViewModel)e.Data.GetData(DragFormat)!;
        var target = row.DataContext as ItemViewModel;
        var group = target != null ? GroupOf(target) : null;
        bool sameGroup = group != null && group.Items.Contains(source);
        bool allowed = target != null && !ReferenceEquals(source, target) && group != null &&
                       (sameGroup ? group.AllowReorder : group.AcceptsItems);
        e.Effects = allowed ? DragDropEffects.Move : DragDropEffects.None;
        // Einfügemarke oben bzw. unten (nicht bei Favoriten: dort wird nur markiert)
        SetInsertMarker(row, allowed && group!.Kind != "fav" ? InsertMarker(row, e) : null);
        e.Handled = true;
    }

    private void Row_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is Border row) SetInsertMarker(row, null);
    }

    private void Row_Drop(object sender, DragEventArgs e)
    {
        if (sender is not Border row) return;
        SetInsertMarker(row, null);
        if (e.Data.GetDataPresent(SettingsWindow.EntryDragFormat) && row.DataContext is ItemViewModel dropTarget && GroupOf(dropTarget) is { } dropGroup)
        {
            e.Handled = true;
            if (e.Data.GetData(SettingsWindow.EntryDragFormat) is string key)
                Vm.MoveKeyToGroup(key, dropGroup, dropGroup.Kind == "fav" ? null : dropTarget, InsertAfter(row, e));
            return;
        }
        if (!e.Data.GetDataPresent(DragFormat) || row.DataContext is not ItemViewModel target) return;
        var source = (ItemViewModel)e.Data.GetData(DragFormat)!;
        var group = GroupOf(target);
        e.Handled = true;
        if (group == null || ReferenceEquals(source, target)) return;
        if (group.Items.Contains(source)) Vm.MoveItem(source, target, InsertAfter(row, e));
        else Vm.MoveToGroup(source, group, group.Kind == "fav" ? null : target, InsertAfter(row, e));
    }

    // ---- Drag & Drop: Gruppen und Dateien aus dem Explorer ----------------------

    private static DragDropEffects ExternalEffect(DragEventArgs e) =>
        e.AllowedEffects.HasFlag(DragDropEffects.Link) ? DragDropEffects.Link
        : e.AllowedEffects.HasFlag(DragDropEffects.Copy) ? DragDropEffects.Copy : DragDropEffects.None;

    private void Group_DragOver(object sender, DragEventArgs e)
    {
        if (sender is not Border box || box.DataContext is not GroupViewModel group) return;
        bool allowed;
        if (e.Data.GetDataPresent(GroupDragFormat))
        {
            ApplyGroupPlan(Vm.CurrentGroupDropPlan, e); // Inhalt einer Gruppe: Ziel vom letzten Gruppenkopf behalten
            return;
        }
        if (e.Data.GetDataPresent(DragFormat))
        {
            var source = (ItemViewModel)e.Data.GetData(DragFormat)!;
            allowed = group.AcceptsItems && !group.Items.Contains(source);
            e.Effects = allowed ? DragDropEffects.Move : DragDropEffects.None;
        }
        else if (e.Data.GetDataPresent(SettingsWindow.EntryDragFormat))
        {
            allowed = group.AcceptsItems;
            e.Effects = allowed ? DragDropEffects.Move : DragDropEffects.None;
        }
        else
        {
            allowed = ExternalDrop.HasData(e.Data);
            e.Effects = allowed ? ExternalEffect(e) : DragDropEffects.None;
        }
        if (allowed)
        {
            box.BorderBrush = (Brush)FindResource("Accent");
            box.Background = (Brush)FindResource("Bg.Hover");
        }
        else Group_DragLeave(sender, e);
        e.Handled = true;
    }

    private void Group_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is not Border box) return;
        // zurück zur Gruppenfarbe aus dem Stil
        box.ClearValue(Border.BorderBrushProperty);
        box.ClearValue(Border.BackgroundProperty);
    }

    private void Group_Drop(object sender, DragEventArgs e)
    {
        if (sender is not Border box || box.DataContext is not GroupViewModel group) return;
        Group_DragLeave(sender, e);
        e.Handled = true;
        if (e.Data.GetDataPresent(GroupDragFormat))
        {
            Vm.CommitGroupDrop();
            return;
        }
        if (e.Data.GetDataPresent(DragFormat))
        {
            Vm.MoveToGroup((ItemViewModel)e.Data.GetData(DragFormat)!, group);
            return;
        }
        if (e.Data.GetData(SettingsWindow.EntryDragFormat) is string entryKey)
        {
            if (group.AcceptsItems) Vm.MoveKeyToGroup(entryKey, group);
            return;
        }
        var targets = ExternalDrop.Read(e.Data);
        if (targets.Count > 0) Vm.DropExternal(targets, group);
    }

    /// <summary>Außerhalb einer Gruppe abgelegt: als eigener Eintrag ohne Gruppe.</summary>
    private void Panel_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(GroupDragFormat))
        {
            ApplyGroupPlan(Vm.CurrentGroupDropPlan, e);
            return;
        }
        e.Effects = e.Data.GetDataPresent(SettingsWindow.EntryDragFormat) ? DragDropEffects.Move
            : !e.Data.GetDataPresent(DragFormat) && !e.Data.GetDataPresent(GroupDragFormat) && ExternalDrop.HasData(e.Data) ? ExternalEffect(e) : DragDropEffects.None;
        e.Handled = true;
    }

    private void Panel_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(SettingsWindow.EntryDragFormat) is string entryKey)
        {
            Vm.DropEntryWithoutGroup(entryKey);
            e.Handled = true;
            return;
        }
        if (e.Data.GetDataPresent(GroupDragFormat))
        {
            Vm.CommitGroupDrop(); // Lücke zeigt, wo die Gruppe landet
            e.Handled = true;
            return;
        }
        if (e.Data.GetDataPresent(DragFormat)) return;
        var targets = ExternalDrop.Read(e.Data);
        if (targets.Count > 0) Vm.DropExternal(targets, null);
        e.Handled = true;
    }
}
