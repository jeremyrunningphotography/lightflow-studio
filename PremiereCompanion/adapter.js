'use strict';

// The production Premiere adapter is isolated so protocol tests exercise the same transaction,
// readback and placement code shipped in the CCX rather than an optimistic in-memory substitute.
function createAdapter(ppro, project, walk, id, nearestPremiereTicks, runtime) {
  const pathKey = value => value.replace(/\\/g, '/').replace(/^\/\/\?\/(?=[a-z]:\/)/i, '').replace(/\/$/, '').toLowerCase();
  const premiereTicks = value => ppro.TickTime.createWithTicks(nearestPremiereTicks(value));
  const transaction = (label, createActions) => {
    let succeeded = false;
    project.lockedAccess(() => {
      succeeded = project.executeTransaction(compound => {
        for (const action of createActions()) compound.addAction(action);
      }, label);
    });
    if (!succeeded) throw new Error(`Premiere could not complete ${label}.`);
  };
  const findExactly = async (itemId, message) => {
    const matches = (await walk(await project.getRootItem())).filter(item => id(item) === itemId);
    if (matches.length !== 1) throw new Error(message);
    return matches[0];
  };
  const parentId = async item => id(await ppro.ProjectItem.cast(item).getParentBin());
  const placeSubclip = async (itemId, targetBin) => {
    const item = await findExactly(itemId, 'Created native Subclip is unavailable for destination placement.');
    if (await parentId(item) !== id(targetBin)) {
      const currentParent = ppro.FolderItem.cast(await ppro.ProjectItem.cast(item).getParentBin());
      transaction('place the native Subclip in the destination bin', () => [
        currentParent.createMoveItemAction(ppro.ProjectItem.cast(item), targetBin)
      ]);
    }
    const verified = await findExactly(itemId, 'Created native Subclip is unavailable after destination placement.');
    if (await parentId(verified) !== id(targetBin))
      throw new Error('Created native Subclip was not placed in the selected destination bin.');
    return verified;
  };
  return {
    activeProject: runtime.activeProject,
    connected: runtime.connected,
    async items() {
      const items = [];
      for (const item of await walk(await project.getRootItem())) {
        let mediaPath = null;
        try { mediaPath = await ppro.ClipProjectItem.cast(item).getMediaFilePath(); } catch (_) { /* bin or non-media */ }
        let parent = null;
        try { parent = await parentId(item); } catch (_) { /* root */ }
        items.push({ id: id(item), name: item.name, mediaPath, parentId: parent });
      }
      return items;
    },
    async targetBin(binId, createName, guard) {
      const root = await project.getRootItem();
      const parent = [root, ...await walk(root)].find(item => id(item) === binId);
      if (!parent) throw new Error('Target bin no longer exists.');
      const bin = ppro.FolderItem.cast(parent);
      if (!createName) return bin;
      const existing = (await bin.getItems()).filter(item => item.name === createName);
      if (existing.length > 1) throw new Error('Target bin name is ambiguous.');
      if (existing.length === 1) return ppro.FolderItem.cast(existing[0]);
      await guard();
      transaction('create the handoff bin', () => [bin.createBinAction(createName, false)]);
      const matches = (await bin.getItems()).filter(item => item.name === createName);
      if (matches.length !== 1) throw new Error('Created bin readback is ambiguous.');
      return ppro.FolderItem.cast(matches[0]);
    },
    importSource: (path, bin) => project.importFiles([path], true, bin, false),
    placeSubclip,
    async projectRange(itemId, range) {
      if (!range || typeof range !== 'object') throw new Error('Lightflow In/Out range is invalid.');
      const sourceDuration = premiereTicks(range.sourceDurationTicks);
      const inPoint = premiereTicks(range.inTicks);
      const outPoint = premiereTicks(range.outTicks);
      if (BigInt(outPoint.ticks) <= BigInt(inPoint.ticks) || BigInt(outPoint.ticks) > BigInt(sourceDuration.ticks))
        throw new Error('Lightflow In/Out range is invalid.');
      const clip = ppro.ClipProjectItem.cast(await findExactly(itemId,
        'Imported source item is unavailable for In/Out projection.'));
      const media = await clip.getMedia();
      const actualDuration = await media.getDuration();
      if (BigInt(actualDuration.ticks) < BigInt(outPoint.ticks))
        throw new Error('The imported media duration does not contain Lightflow’s saved Out point.');
      transaction('apply the source In/Out points', () => [clip.createSetInOutPointsAction(inPoint, outPoint)]);
    },
    async clearRange(itemId) {
      const clip = ppro.ClipProjectItem.cast(await findExactly(itemId,
        'Imported source item is unavailable for In/Out projection.'));
      transaction('clear the source In/Out points', () => [clip.createClearInOutPointsAction()]);
    },
    async createSubclip(sourceItemId, spec, targetBin) {
      const beforeItems = await walk(await project.getRootItem());
      const sourceMatches = beforeItems.filter(item => id(item) === sourceItemId);
      if (sourceMatches.length !== 1) throw new Error('Mapped source is unavailable for native Subclip creation.');
      const clip = ppro.ClipProjectItem.cast(sourceMatches[0]);
      const media = await clip.getMedia();
      const duration = await media.getDuration();
      const start = spec.range ? premiereTicks(spec.range.inTicks) : ppro.TickTime.createWithTicks('0');
      const end = spec.range ? premiereTicks(spec.range.outTicks) : duration;
      if (BigInt(end.ticks) <= BigInt(start.ticks) || BigInt(end.ticks) > BigInt(duration.ticks))
        throw new Error('The native Subclip range is outside the imported source duration.');
      const before = new Set(beforeItems.map(id));
      transaction('create the native Subclip', () => [clip.createSubClipAction(spec.name, start, end,
        spec.hardBoundaries === true, { takeVideo: spec.takeVideo === true, takeAudio: spec.takeAudio === true })]);
      let added = (await walk(await project.getRootItem())).filter(item => !before.has(id(item)) && item.name === spec.name);
      if (added.length !== 1) throw new Error('Native Subclip creation did not produce exactly one readable project item.');
      const created = added[0];
      const createdId = id(created);
      if (createdId === sourceItemId || pathKey(await ppro.ClipProjectItem.cast(created).getMediaFilePath())
          !== pathKey(await clip.getMediaFilePath()))
        throw new Error('Native Subclip readback did not identify a distinct item for the mapped source.');
      await placeSubclip(createdId, targetBin);
      return createdId;
    }
  };
}

module.exports = { createAdapter };
