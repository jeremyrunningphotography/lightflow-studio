using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class BrowserCollectionsTests
{
    [Theory]
    [InlineData(true, 2, true)]
    [InlineData(true, 3, true)]
    [InlineData(false, 2, false)]
    [InlineData(true, 1, false)]
    public void AssetDragSelection_DefersPlainClickCollapseOnlyForAnExistingMultiSelection(
        bool tileSelected, int selectionCount, bool expected) =>
        Assert.Equal(expected, BrowserAssetDragSelection.ShouldDeferSingleSelection(
            tileSelected, selectionCount, shiftPressed: false, controlPressed: false));

    [Fact]
    public void AssetDragSelection_ModifiedClicksKeepEstablishedCtrlShiftSemantics()
    {
        Assert.False(BrowserAssetDragSelection.ShouldDeferSingleSelection(true, 3, shiftPressed: true, controlPressed: false));
        Assert.False(BrowserAssetDragSelection.ShouldDeferSingleSelection(true, 3, shiftPressed: false, controlPressed: true));
    }

    [Fact]
    public void DragFromEitherSelectedTileCarriesTheEntireStableAssetSelection()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        IReadOnlyList<Guid> selected = [first, second];

        Assert.Equal(selected, BrowserAssetDragSelection.AssetIdsForDrag(true, first, selected));
        Assert.Equal(selected, BrowserAssetDragSelection.AssetIdsForDrag(true, second, selected));
        Assert.Equal([Guid.Empty], BrowserAssetDragSelection.AssetIdsForDrag(false, Guid.Empty, selected));
    }

    [Fact]
    public void GenuineCollectionPointerActivationOverridesMatchingDelayedRevealMarker()
    {
        var collection = new BrowserCollectionNode(Collection("Target", 0));
        var interactive = BrowserCollectionActivation.IsInteractive(collection, collection, keyboardSelectionPending: false);

        Assert.True(interactive);
        Assert.False(BrowserCollectionActivation.ShouldIgnoreDelayedReveal(collection, collection, interactive));
        Assert.True(BrowserCollectionActivation.ShouldIgnoreDelayedReveal(collection, collection, interactive: false));
    }

    [Fact]
    public void CollectionActivation_KeyboardIntentIsExplicitRatherThanInferredFromLingeringFocus()
    {
        var collection = new BrowserCollectionNode(Collection("Collection", 0));

        Assert.True(BrowserCollectionActivation.IsInteractive(collection, null, keyboardSelectionPending: true));
        Assert.False(BrowserCollectionActivation.IsInteractive(collection, null, keyboardSelectionPending: false));
    }

    [Theory]
    [InlineData(1, 0, 1, 1, "Added 1 media item to Picks")]
    [InlineData(0, 3, 3, 1, "3 media items are already in Picks")]
    [InlineData(6, 0, 3, 2, "Added 6 media items to 2 Collections")]
    [InlineData(0, 2, 1, 2, "2 media items were already present")]
    [InlineData(4, 2, 3, 2, "Added 4 media items to 2 Collections • 2 media items were already present")]
    public void MembershipFeedback_IsConciseNonTechnicalAndTruthful(int added, int duplicates,
        int assets, int collections, string expected)
    {
        var result = CollectionMembershipFeedback.ForAdd(added, duplicates, assets, collections,
            collections == 1 ? "Picks" : null);

        Assert.Equal(expected, result);
        Assert.DoesNotContain("Source files", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("paths", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("asset", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AssetMembershipDrop_AcceptsCollectionsAndRejectsSets()
    {
        var payload = new BrowserAssetDragPayload([Guid.NewGuid(), Guid.NewGuid()]);

        Assert.True(BrowserCollectionMembershipInteraction.CanDrop(payload, new BrowserCollectionNode(Collection("Target", 0))));
        Assert.False(BrowserCollectionMembershipInteraction.CanDrop(payload, new BrowserCollectionNode(Set("Organizer", 0))));
        Assert.False(BrowserCollectionMembershipInteraction.CanDrop(new BrowserAssetDragPayload([]), new BrowserCollectionNode(Collection("Target", 0))));
    }

    [Fact]
    public void MultiAssetManualMove_PreservesSelectionOrderAndOtherMemberships()
    {
        var ids = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray();

        var reordered = BrowserCollectionMembershipInteraction.MoveBefore(ids, [ids[3], ids[1]], ids[0]);

        Assert.Equal([ids[3], ids[1], ids[0], ids[2], ids[4]], reordered);
    }

    [Fact]
    public void RightClick_TargetsClickedCollectionInsteadOfPreviouslySelectedSet()
    {
        var selectedSet = new BrowserCollectionNode(Set("Selected set", 0));
        var clickedCollection = new BrowserCollectionNode(Collection("Clicked collection", 0, selectedSet.Id));

        var target = BrowserCollectionInteraction.ContextTarget(clickedCollection, selectedSet);

        Assert.Same(clickedCollection, target);
        Assert.True(target.IsCollection);
    }

    [Fact]
    public void Placement_DefaultsToContainingSetButAllowsTopLevelOverride()
    {
        var set = new BrowserCollectionNode(Set("Set", 0));
        var collection = new BrowserCollectionNode(Collection("Collection", 0, set.Id));

        Assert.Equal(set.Id, BrowserCollectionPlacement.SuggestedParent(collection));
        Assert.Equal(set.Id, BrowserCollectionPlacement.SuggestedParent(set));
        Assert.Null(BrowserCollectionPlacement.SuggestedParent(null));
        var options = BrowserCollectionPlacement.Options([set]);
        Assert.Null(options[0].CollectionSetId);
        Assert.Equal("Top level", options[0].DisplayName);
        Assert.Equal(set.Id, options[1].CollectionSetId);
    }

    [Fact]
    public void ActiveScopeSelection_HasOneAuthoritativeKind()
    {
        var selection = new BrowserScopeSelection();
        selection.ActivateFolder();
        Assert.Equal(BrowserScopeSelectionKind.Folder, selection.Active);
        selection.ActivateCollection();
        Assert.Equal(BrowserScopeSelectionKind.Collection, selection.Active);
    }

    [Fact]
    public void CollectionToFolderTransition_AcceptsPointerSelectionButRejectsDelayedReveal()
    {
        var selection = new BrowserScopeSelection();
        var clicked = new BrowserTreeNode("Clicked", @"C:\media\clicked");
        var delayed = new BrowserTreeNode("Delayed", @"C:\media\delayed");
        selection.ActivateCollection();

        Assert.False(selection.ShouldActivateFolder(delayed, null, false, delayed));
        Assert.True(selection.ShouldActivateFolder(clicked, clicked, false, delayed));
        selection.ActivateFolder();
        Assert.Equal(BrowserScopeSelectionKind.Folder, selection.Active);
    }

    [Fact]
    public void FolderToCollectionToAnotherFolder_RemainsBidirectional()
    {
        var selection = new BrowserScopeSelection();
        var first = new BrowserTreeNode("First", @"C:\media\first");
        var second = new BrowserTreeNode("Second", @"C:\media\second");
        selection.ActivateFolder();
        selection.ActivateCollection();
        Assert.True(selection.ShouldActivateFolder(first, first, false, null));
        selection.ActivateFolder();
        Assert.True(selection.ShouldActivateFolder(second, second, false, null));
    }

    [Fact]
    public void DragWheelSession_PreservesPayloadAndRetargetsOnlyAfterAnActualSidebarScroll()
    {
        var dragged = new BrowserCollectionNode(Collection("Dragged", 0));
        var session = new BrowserCollectionDragSession();
        BrowserCollectionNode? retargeted = null;
        session.Begin(dragged);

        Assert.False(session.RouteWheel(false, () => true, node => retargeted = node));
        Assert.False(session.RouteWheel(true, () => false, node => retargeted = node));
        Assert.Null(retargeted);
        Assert.True(session.RouteWheel(true, () => true, node => retargeted = node));
        Assert.Same(dragged, session.Payload);
        Assert.Same(dragged, retargeted);

        session.End();
        Assert.False(session.RouteWheel(true, () => true, node => retargeted = node));
        Assert.Null(session.Payload);
    }

    [Fact]
    public void DragIntent_DistinguishesSiblingInsertionFromDropIntoSet()
    {
        var first = new BrowserCollectionNode(Collection("First", 0));
        var second = new BrowserCollectionNode(Collection("Second", 1));
        var set = new BrowserCollectionNode(Set("Set", 0));
        var nestedCollection = new BrowserCollectionNode(Collection("Nested", 0, set.Id));

        Assert.Equal(BrowserCollectionDropKind.InsertBefore, BrowserCollectionInteraction.DropAt(first, second, 0.1).Kind);
        Assert.Equal(BrowserCollectionDropKind.InsertAfter, BrowserCollectionInteraction.DropAt(first, second, 0.9).Kind);
        Assert.Equal(BrowserCollectionDropKind.IntoSet, BrowserCollectionInteraction.DropAt(first, set, 0.5).Kind);
        Assert.Equal(BrowserCollectionDropKind.InsertBefore, BrowserCollectionInteraction.DropAt(first, set, 0.1).Kind);
        Assert.Equal(BrowserCollectionDropKind.InsertAfter, BrowserCollectionInteraction.DropAt(first, set, 0.9).Kind);
        Assert.Equal(BrowserCollectionDropKind.InsertBefore, BrowserCollectionInteraction.DropAt(first, nestedCollection, 0.1).Kind);
    }

    [Fact]
    public void NameSort_OrdersOneMixedVisibleSiblingList()
    {
        var nodes = new[] { new BrowserCollectionNode(Collection("Zulu", 0)), new BrowserCollectionNode(Set("Bravo", 1)),
            new BrowserCollectionNode(Collection("alpha", 2)) };
        Assert.Equal(["alpha", "Bravo", "Zulu"], BrowserCollectionInteraction.OrderByName(nodes, false).Select(node => node.Name));
        Assert.Equal(["Zulu", "Bravo", "alpha"], BrowserCollectionInteraction.OrderByName(nodes, true).Select(node => node.Name));
    }

    [Fact]
    public void HierarchyDrop_AllowsSetTargetsAndTopLevelWhileRejectingCyclesAndCollectionTargets()
    {
        var parent = new BrowserCollectionNode(Set("Parent", 0));
        var child = new BrowserCollectionNode(Set("Child", 0, parent.Id));
        var other = new BrowserCollectionNode(Set("Other", 1));
        var collection = new BrowserCollectionNode(Collection("Collection", 0, parent.Id));
        parent.Children.Add(child);
        parent.Children.Add(collection);

        Assert.True(BrowserCollectionInteraction.CanDrop(collection, other));
        Assert.True(BrowserCollectionInteraction.CanDrop(child, other));
        Assert.True(BrowserCollectionInteraction.CanDrop(child, null));
        Assert.False(BrowserCollectionInteraction.CanDrop(parent, child));
        Assert.False(BrowserCollectionInteraction.CanDrop(other, collection));
    }

    [Fact]
    public void Tree_PresentsOneMixedOrderAtEachLevelAndRestoresExpansionSelection()
    {
        var top = Set("Top", 1);
        var child = Set("Child", 1, top.CollectionSetId);
        var nested = Collection("Nested", 0, top.CollectionSetId);
        var rootCollection = Collection("Root collection", 0);
        var model = new BrowserCollectionTreeModel();

        model.Populate([top, child], [rootCollection, nested], new HashSet<Guid> { top.CollectionSetId }, nested.CollectionId);

        Assert.Equal([rootCollection.CollectionId, top.CollectionSetId], model.Roots.Select(node => node.Id));
        Assert.True(model.Roots[1].IsExpanded);
        Assert.Equal([nested.CollectionId, child.CollectionSetId], model.Roots[1].Children.Select(node => node.Id));
        Assert.Equal(nested.CollectionId, model.SelectedNode!.Id);
    }

    [Fact]
    public void EverySet_IsDirectIntoTargetWhetherEmptyOrPopulated()
    {
        var draggedCollection = new BrowserCollectionNode(Collection("Dragged", 0));
        var empty = new BrowserCollectionNode(Set("Empty", 1));
        var populated = new BrowserCollectionNode(Set("Populated", 2));
        var expandedEmpty = new BrowserCollectionNode(Set("Expanded empty", 3)) { IsExpanded = true };
        var expandedPopulated = new BrowserCollectionNode(Set("Expanded populated", 4)) { IsExpanded = true };
        populated.Children.Add(new BrowserCollectionNode(Collection("Existing", 0, populated.Id)));
        expandedPopulated.Children.Add(new BrowserCollectionNode(Collection("Existing", 0, expandedPopulated.Id)));

        Assert.Equal(BrowserCollectionDropKind.IntoSet,
            BrowserCollectionInteraction.DropAt(draggedCollection, empty, 0.5).Kind);
        Assert.Equal(BrowserCollectionDropKind.IntoSet,
            BrowserCollectionInteraction.DropAt(draggedCollection, populated, 0.5).Kind);
        Assert.Equal(BrowserCollectionDropKind.IntoSet,
            BrowserCollectionInteraction.DropAt(draggedCollection, expandedEmpty, 0.5).Kind);
        Assert.Equal(BrowserCollectionDropKind.IntoSet,
            BrowserCollectionInteraction.DropAt(draggedCollection, expandedPopulated, 0.5).Kind);
    }

    [Fact]
    public void ExpandedSet_HeaderBottomAndChildLinesResolveInsideItsVisibleSubtree()
    {
        var set = new BrowserCollectionNode(Set("Set", 0)) { IsExpanded = true };
        var first = new BrowserCollectionNode(Collection("First", 0, set.Id));
        var second = new BrowserCollectionNode(Collection("Second", 1, set.Id));
        set.Children.Add(first);
        set.Children.Add(second);
        var sibling = new BrowserCollectionNode(Collection("Sibling", 1));
        var dragged = new BrowserCollectionNode(Collection("Dragged", 2));
        BrowserCollectionNode[] roots = [set, sibling, dragged];

        Assert.Equal(new(set.Id, 0), BrowserCollectionInteraction.ResolveInsertion(
            roots, dragged, set, BrowserCollectionDropKind.InsertAfter));
        Assert.Equal(new(set.Id, 1), BrowserCollectionInteraction.ResolveInsertion(
            roots, dragged, second, BrowserCollectionDropKind.InsertBefore));
        Assert.Equal(new(set.Id, 1), BrowserCollectionInteraction.ResolveInsertion(
            roots, dragged, first, BrowserCollectionDropKind.InsertAfter));
        var headerBoundary = BrowserCollectionInteraction.ResolveInsertionChoices(
            roots, dragged, set, BrowserCollectionDropKind.InsertAfter);
        Assert.Single(headerBoundary);
        Assert.Equal(new(set.Id, 0), headerBoundary[0].Destination);
        Assert.Same(first, headerBoundary[0].Target);
        Assert.Equal(BrowserCollectionDropKind.InsertBefore, headerBoundary[0].Kind);
    }

    [Fact]
    public void FinalVisibleDescendant_ExposesAppendInsideAndInsertAfterOutsideDestinations()
    {
        var set = new BrowserCollectionNode(Set("Set", 0)) { IsExpanded = true };
        var first = new BrowserCollectionNode(Collection("First", 0, set.Id));
        var last = new BrowserCollectionNode(Collection("Last", 1, set.Id));
        set.Children.Add(first);
        set.Children.Add(last);
        var sibling = new BrowserCollectionNode(Collection("Sibling", 1));
        var dragged = new BrowserCollectionNode(Collection("Dragged", 2));
        BrowserCollectionNode[] roots = [set, sibling, dragged];

        Assert.Equal(new(set.Id, 2), BrowserCollectionInteraction.ResolveInsertion(
            roots, dragged, last, BrowserCollectionDropKind.InsertAfter));
        Assert.Equal(new(null, 1), BrowserCollectionInteraction.ResolveInsertion(
            roots, dragged, sibling, BrowserCollectionDropKind.InsertBefore));

        var fromChildSide = BrowserCollectionInteraction.ResolveInsertionChoices(
            roots, dragged, last, BrowserCollectionDropKind.InsertAfter);
        var fromParentSide = BrowserCollectionInteraction.ResolveInsertionChoices(
            roots, dragged, sibling, BrowserCollectionDropKind.InsertBefore);
        Assert.Equal(2, fromChildSide.Count);
        Assert.Equal(fromChildSide, fromParentSide);
        Assert.Equal([new BrowserCollectionInsertionDestination(set.Id, 2),
            new BrowserCollectionInsertionDestination(null, 1)],
            fromChildSide.Select(choice => choice.Destination));
        Assert.Equal([BrowserCollectionDropKind.InsertAfter, BrowserCollectionDropKind.InsertBefore],
            fromChildSide.Select(choice => choice.Kind));
        Assert.NotEqual(fromChildSide[0].Target.ParentSetId, fromChildSide[1].Target.ParentSetId);
    }

    [Fact]
    public void CollapsedSet_HeaderBottomResolvesAfterSetAtParentLevel()
    {
        var set = new BrowserCollectionNode(Set("Set", 0));
        set.Children.Add(new BrowserCollectionNode(Collection("Hidden", 0, set.Id)));
        var sibling = new BrowserCollectionNode(Collection("Sibling", 1));
        var dragged = new BrowserCollectionNode(Collection("Dragged", 2));
        BrowserCollectionNode[] roots = [set, sibling, dragged];

        Assert.Equal(new(null, 1), BrowserCollectionInteraction.ResolveInsertion(
            roots, dragged, set, BrowserCollectionDropKind.InsertAfter));
        Assert.Single(BrowserCollectionInteraction.ResolveInsertionChoices(
            roots, dragged, set, BrowserCollectionDropKind.InsertAfter));
    }

    [Fact]
    public void NestedExpandedBoundaries_ClimbOnlyPastExhaustedVisibleSubtrees()
    {
        var outer = new BrowserCollectionNode(Set("Outer", 0)) { IsExpanded = true };
        var nested = new BrowserCollectionNode(Set("Nested", 0, outer.Id)) { IsExpanded = true };
        var leaf = new BrowserCollectionNode(Collection("Leaf", 0, nested.Id));
        var outerLast = new BrowserCollectionNode(Collection("Outer last", 1, outer.Id));
        nested.Children.Add(leaf);
        outer.Children.Add(nested);
        outer.Children.Add(outerLast);
        var rootSibling = new BrowserCollectionNode(Collection("Root sibling", 1));
        var dragged = new BrowserCollectionNode(Set("Dragged set", 2));
        BrowserCollectionNode[] roots = [outer, rootSibling, dragged];

        Assert.Equal(new(nested.Id, 1), BrowserCollectionInteraction.ResolveInsertion(
            roots, dragged, leaf, BrowserCollectionDropKind.InsertAfter));
        Assert.Equal(new(outer.Id, 2), BrowserCollectionInteraction.ResolveInsertion(
            roots, dragged, outerLast, BrowserCollectionDropKind.InsertAfter));

        Assert.Equal([new BrowserCollectionInsertionDestination(nested.Id, 1),
            new BrowserCollectionInsertionDestination(outer.Id, 1)],
            BrowserCollectionInteraction.ResolveInsertionChoices(
                    roots, dragged, leaf, BrowserCollectionDropKind.InsertAfter)
                .Select(choice => choice.Destination));
        Assert.Equal([new BrowserCollectionInsertionDestination(outer.Id, 2),
            new BrowserCollectionInsertionDestination(null, 1)],
            BrowserCollectionInteraction.ResolveInsertionChoices(
                    roots, dragged, outerLast, BrowserCollectionDropKind.InsertAfter)
                .Select(choice => choice.Destination));
    }

    [Fact]
    public void CollectionAndSetMovesResolveValidDestinationsWhileSetCyclesRemainRejected()
    {
        var parent = new BrowserCollectionNode(Set("Parent", 0)) { IsExpanded = true };
        var childSet = new BrowserCollectionNode(Set("Child", 0, parent.Id));
        var childCollection = new BrowserCollectionNode(Collection("Child collection", 1, parent.Id));
        parent.Children.Add(childSet);
        parent.Children.Add(childCollection);
        var collection = new BrowserCollectionNode(Collection("Collection", 1));
        var otherSet = new BrowserCollectionNode(Set("Other set", 2));
        BrowserCollectionNode[] roots = [parent, collection, otherSet];

        Assert.Equal(new(parent.Id, 0), BrowserCollectionInteraction.ResolveInsertion(
            roots, collection, parent, BrowserCollectionDropKind.InsertAfter));
        Assert.Equal(new(parent.Id, 1), BrowserCollectionInteraction.ResolveInsertion(
            roots, otherSet, childCollection, BrowserCollectionDropKind.InsertBefore));
        Assert.Null(BrowserCollectionInteraction.ResolveInsertion(
            roots, parent, childCollection, BrowserCollectionDropKind.InsertBefore));
    }

    [Fact]
    public void TrailingExpandedRootSet_ExposesAppendInsideAndInsertAfterWithoutFollowingSibling()
    {
        var set = new BrowserCollectionNode(Set("Set", 0)) { IsExpanded = true };
        var child = new BrowserCollectionNode(Collection("Child", 0, set.Id));
        set.Children.Add(child);
        var dragged = new BrowserCollectionNode(Collection("Dragged", 1));

        var choices = BrowserCollectionInteraction.ResolveTrailingInsertionChoices([set, dragged], dragged);

        Assert.Equal([new BrowserCollectionInsertionDestination(set.Id, 1),
            new BrowserCollectionInsertionDestination(null, 1)], choices.Select(choice => choice.Destination));
    }

    [Fact]
    public void TrailingExpandedSetWhoseOnlyChildIsDragged_StillExposesFirstChildAndParentDestinations()
    {
        var set = new BrowserCollectionNode(Set("Set", 0)) { IsExpanded = true };
        var dragged = new BrowserCollectionNode(Collection("Dragged", 0, set.Id));
        set.Children.Add(dragged);

        var choices = BrowserCollectionInteraction.ResolveTrailingInsertionChoices([set], dragged);

        Assert.Equal([new BrowserCollectionInsertionDestination(set.Id, 0),
            new BrowserCollectionInsertionDestination(null, 1)], choices.Select(choice => choice.Destination));
    }

    [Fact]
    public void TrailingNestedExpandedSets_ExposeEveryExhaustedAncestorLevel()
    {
        var outer = new BrowserCollectionNode(Set("Outer", 0)) { IsExpanded = true };
        var nested = new BrowserCollectionNode(Set("Nested", 0, outer.Id)) { IsExpanded = true };
        var leaf = new BrowserCollectionNode(Collection("Leaf", 0, nested.Id));
        nested.Children.Add(leaf);
        outer.Children.Add(nested);
        var dragged = new BrowserCollectionNode(Collection("Dragged", 1));

        var choices = BrowserCollectionInteraction.ResolveTrailingInsertionChoices([outer, dragged], dragged);

        Assert.Equal([new BrowserCollectionInsertionDestination(nested.Id, 1),
            new BrowserCollectionInsertionDestination(outer.Id, 1),
            new BrowserCollectionInsertionDestination(null, 1)], choices.Select(choice => choice.Destination));
    }

    [Fact]
    public void TrailingOrdinaryRootCollection_ExposesRootAppendAndHorizontalSelectionChoosesOneLevel()
    {
        var first = new BrowserCollectionNode(Collection("First", 0));
        var last = new BrowserCollectionNode(Collection("Last", 1));
        var dragged = new BrowserCollectionNode(Collection("Dragged", 2));
        var rootChoice = Assert.Single(BrowserCollectionInteraction.ResolveTrailingInsertionChoices(
            [first, last, dragged], dragged));
        Assert.Equal(new BrowserCollectionInsertionDestination(null, 2), rootChoice.Destination);

        var set = new BrowserCollectionNode(Set("Set", 0));
        var nestedChoice = new BrowserCollectionInsertionChoice(last, BrowserCollectionDropKind.InsertAfter,
            new(set.Id, 1));
        Assert.Same(rootChoice, BrowserCollectionInteraction.SelectTrailingInsertionChoice(
            [(nestedChoice, 60d), (rootChoice, 20d)], 30));
        Assert.Same(nestedChoice, BrowserCollectionInteraction.SelectTrailingInsertionChoice(
            [(nestedChoice, 60d), (rootChoice, 20d)], 55));
    }

    [Fact]
    public void DragHover_ExpandsEligibleCollapsedSetOnlyAfterDeterministicDwellWithoutMutation()
    {
        var dragged = new BrowserCollectionNode(Collection("Dragged", 0));
        var target = new BrowserCollectionNode(Set("Target", 1));
        var originalParent = dragged.ParentSetId;
        var originalOrdinal = dragged.Ordinal;
        var start = DateTimeOffset.UtcNow;
        var hover = new BrowserCollectionDragHover();

        Assert.True(hover.Track(dragged, target, BrowserCollectionDropKind.IntoSet, start));
        Assert.Null(hover.TakeReady(start + BrowserCollectionDragHover.Dwell - TimeSpan.FromMilliseconds(1)));
        Assert.Same(target, hover.TakeReady(start + BrowserCollectionDragHover.Dwell));
        Assert.Equal(originalParent, dragged.ParentSetId);
        Assert.Equal(originalOrdinal, dragged.Ordinal);
        Assert.Null(hover.PendingTarget);
    }

    [Fact]
    public void DragHover_LeavingEarlyCancelsAndChangingTargetRestartsDwell()
    {
        var dragged = new BrowserCollectionNode(Collection("Dragged", 0));
        var first = new BrowserCollectionNode(Set("First", 1));
        var second = new BrowserCollectionNode(Set("Second", 2));
        var start = DateTimeOffset.UtcNow;
        var hover = new BrowserCollectionDragHover();

        hover.Track(dragged, first, BrowserCollectionDropKind.IntoSet, start);
        hover.Track(dragged, null, BrowserCollectionDropKind.None, start + TimeSpan.FromSeconds(1));
        Assert.Null(hover.TakeReady(start + TimeSpan.FromSeconds(3)));

        hover.Track(dragged, first, BrowserCollectionDropKind.IntoSet, start);
        Assert.True(hover.Track(dragged, second, BrowserCollectionDropKind.IntoSet,
            start + TimeSpan.FromSeconds(1)));
        Assert.Null(hover.TakeReady(start + BrowserCollectionDragHover.Dwell));
        Assert.Same(second, hover.TakeReady(start + TimeSpan.FromSeconds(1) + BrowserCollectionDragHover.Dwell));
    }

    [Fact]
    public void DragHover_SupportsRecursiveDrillDownAndDoesNotRetriggerExpandedSet()
    {
        var dragged = new BrowserCollectionNode(Collection("Dragged", 0));
        var outer = new BrowserCollectionNode(Set("Outer", 1));
        var nested = new BrowserCollectionNode(Set("Nested", 0, outer.Id));
        outer.Children.Add(nested);
        var hover = new BrowserCollectionDragHover();
        var start = DateTimeOffset.UtcNow;

        hover.Track(dragged, outer, BrowserCollectionDropKind.IntoSet, start);
        Assert.Same(outer, hover.TakeReady(start + BrowserCollectionDragHover.Dwell));
        outer.IsExpanded = true;
        Assert.False(hover.Track(dragged, outer, BrowserCollectionDropKind.IntoSet,
            start + BrowserCollectionDragHover.Dwell));
        Assert.Null(hover.PendingTarget);

        Assert.True(hover.Track(dragged, nested, BrowserCollectionDropKind.IntoSet,
            start + TimeSpan.FromSeconds(2)));
        Assert.Same(nested, hover.TakeReady(start + TimeSpan.FromSeconds(2) + BrowserCollectionDragHover.Dwell));
    }

    [Fact]
    public void DragHover_RejectsCycleDestinationAndNonCenterInsertionRegions()
    {
        var parent = new BrowserCollectionNode(Set("Parent", 0));
        var child = new BrowserCollectionNode(Set("Child", 0, parent.Id));
        parent.Children.Add(child);
        var hover = new BrowserCollectionDragHover();
        var now = DateTimeOffset.UtcNow;

        Assert.False(hover.Track(parent, child, BrowserCollectionDropKind.IntoSet, now));
        Assert.False(hover.Track(parent, child, BrowserCollectionDropKind.InsertBefore, now));
        Assert.Null(hover.PendingTarget);
        Assert.Null(hover.TakeReady(now + TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Scope_UsesMembershipOnlyPreservesOrderAndTruthfullyRetainsUnavailableMembers()
    {
        var collection = Collection("Picks", 0);
        var available = Asset("same.jpg", MediaAssetSourceStatus.Available);
        var missing = Asset("same.jpg", MediaAssetSourceStatus.Missing);
        var memberships = new[] { Membership(collection.CollectionId, missing.AssetId, 0), Membership(collection.CollectionId, available.AssetId, 1) };
        var service = new BrowserCollectionScopeService(new FakeCollections(collection, memberships),
            new FakeAssets([available, missing]), new FakeRoots([available, missing]), MediaTypeRegistry.CreateDefault(), () => null);

        var scope = await service.LoadAsync(collection.CollectionId);

        Assert.Equal([missing.AssetId, available.AssetId], scope.Entries.Select(entry => entry.AssetId));
        Assert.Equal([false, true], scope.Entries.Select(entry => entry.IsAvailable));
        Assert.Equal(1, scope.UnavailableCount);
        Assert.All(scope.Entries, entry => Assert.StartsWith("asset:", entry.StableKey));

        var grid = new BrowserGridModel();
        grid.Populate(scope.Entries);
        Assert.Equal(2, grid.TotalCount); // equal relative paths across roots never collide in Collection scope
        Assert.Contains(grid.Tiles, tile => !tile.IsAvailable && tile.AssetId == missing.AssetId);
    }

    [Fact]
    public async Task Scope_LargeCollectionFeedsExistingGridQuerySelectionAndVirtualizedRows()
    {
        var collection = Collection("Large", 0);
        var assets = Enumerable.Range(0, 2500).Select(index => Asset($"image-{index:D4}.jpg", MediaAssetSourceStatus.Available)).ToArray();
        var memberships = assets.Select((asset, index) => Membership(collection.CollectionId, asset.AssetId, index)).ToArray();
        var service = new BrowserCollectionScopeService(new FakeCollections(collection, memberships),
            new FakeAssets(assets), new FakeRoots(assets), MediaTypeRegistry.CreateDefault(), () => null);

        var scope = await service.LoadAsync(collection.CollectionId);
        var grid = new BrowserGridModel();
        grid.SetColumns(8);
        grid.Populate(scope.Entries);
        grid.SetQuery(BrowserQuery.Default with { SearchText = "image-249" });
        grid.SelectSingle(0);

        Assert.Equal(2500, grid.TotalCount);
        Assert.Equal(10, grid.VisibleCount);
        Assert.Equal(2, grid.Rows.Count);
        Assert.Single(grid.SelectedAssetIdsInBrowserOrder);
    }

    private static CollectionSet Set(string name, int ordinal, Guid? parent = null) =>
        new(Guid.NewGuid(), parent, name, ordinal, 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
    private static MediaCollection Collection(string name, int ordinal, Guid? parent = null) =>
        new(Guid.NewGuid(), parent, name, ordinal, 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
    private static CollectionMembership Membership(Guid collectionId, Guid assetId, int ordinal) =>
        new(collectionId, assetId, ordinal, 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
    private static MediaAsset Asset(string path, MediaAssetSourceStatus status) =>
        new(Guid.NewGuid(), Guid.NewGuid(), path, path.ToUpperInvariant(), "image", 10, DateTimeOffset.UtcNow.UtcTicks,
            null, status, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private sealed class FakeAssets(IReadOnlyList<MediaAsset> assets) : IMediaAssetService
    {
        public Task<IReadOnlyList<MediaAsset>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult(assets);
        public Task<MediaAssetOperationResult> CreateAsync(Guid rootId, string relativePath, string mediaType = "unknown", CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MediaAssetResolution?> GetAsync(Guid assetId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MediaAssetResolution?> FindAsync(Guid rootId, string relativePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MediaAssetOperationResult> ObserveAsync(Guid assetId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> MarkMissingAsync(IReadOnlyCollection<Guid> assetIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeRoots(IReadOnlyList<MediaAsset> assets) : IMediaRootService
    {
        public Task<IReadOnlyList<MediaRootInfo>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MediaRootInfo>>(assets.Select(asset => asset.RootId).Distinct()
                .Select(id => new MediaRootInfo(id, id.ToString(), @"C:\media", MediaRootAvailability.Online)).ToArray());
        public Task<MediaRootInfo?> GetAsync(Guid rootId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MediaRootChangeResult> CreateAsync(string displayName, string physicalPath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MediaRootChangeResult> RenameAsync(Guid rootId, string displayName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MediaRootChangeResult> RemapAsync(Guid rootId, string physicalPath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MediaPathResolution> ResolveAsync(Guid rootId, string relativePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeCollections(MediaCollection collection, IReadOnlyList<CollectionMembership> memberships) : ICollectionOrganizationService
    {
        public Task<MediaCollection?> GetCollectionAsync(Guid collectionId, CancellationToken cancellationToken = default) => Task.FromResult<MediaCollection?>(collection);
        public Task<IReadOnlyList<CollectionMembership>> ListMembershipsAsync(Guid collectionId, CancellationToken cancellationToken = default) => Task.FromResult(memberships);
        public Task<IReadOnlyList<CollectionSet>> ListSetsAsync(Guid? parentCollectionSetId = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<MediaCollection>> ListCollectionsAsync(Guid? parentCollectionSetId = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CollectionSet?> GetSetAsync(Guid collectionSetId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CollectionSet> CreateSetAsync(string name, Guid? parentCollectionSetId = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MediaCollection> CreateCollectionAsync(string name, Guid? parentCollectionSetId = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CollectionSet> RenameSetAsync(Guid collectionSetId, long expectedRevision, string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MediaCollection> RenameCollectionAsync(Guid collectionId, long expectedRevision, string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CollectionSet> ReparentSetAsync(Guid collectionSetId, long expectedRevision, Guid? parentCollectionSetId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MediaCollection> ReparentCollectionAsync(Guid collectionId, long expectedRevision, Guid? parentCollectionSetId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteSetAsync(Guid collectionSetId, long expectedRevision, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteCollectionAsync(Guid collectionId, long expectedRevision, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ReorderHierarchyAsync(Guid? parentCollectionSetId, IReadOnlyList<CollectionHierarchyOrder> order, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CollectionMembershipCreateResult> AddMembershipAsync(Guid collectionId, Guid assetId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<CollectionMembershipCreateResult>> AddMembershipsAsync(Guid collectionId, IReadOnlyList<Guid> assetIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RemoveMembershipAsync(Guid collectionId, Guid assetId, long expectedRevision, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RemoveMembershipsAsync(Guid collectionId, IReadOnlyList<CollectionOrder> memberships, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<CollectionMembership>> ReorderMembershipsAsync(Guid collectionId, IReadOnlyList<CollectionOrder> order, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
