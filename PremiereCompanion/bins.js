'use strict';
const { sameProject } = require('./handoff.js');

// Cache only within the exact project GUID + saved-path destination. Busy pulses
// must never label the old project's bins as belonging to a newly active project.
class ProjectBins {
  constructor() { this.project = null; this.bins = []; }
  async read(project, discover, enumerate) {
    if (!project || !sameProject(project, this.project)) {
      this.project = project;
      this.bins = [];
    }
    if (project && discover) {
      const bins = await enumerate();
      if (bins.length > 1000) throw new Error('Project exceeds the supported limit of 1,000 bins.');
      if (sameProject(project, this.project)) this.bins = bins;
      return bins;
    }
    return this.bins;
  }
}

async function enumerateBins(root, asFolder, itemId) {
  const bins = [{ id: itemId(root), name: 'Project root' }];
  let visited = 0;
  async function visit(parent, prefix, depth) {
    if (depth > 50) throw new Error('Project bin nesting exceeds the supported limit.');
    for (const item of await parent.getItems()) {
      if (++visited > 50000) throw new Error('Project exceeds the supported item limit.');
      let bin;
      try { bin = asFolder(item); } catch (_) { /* Media items are not folders. */ }
      if (!bin) continue;
      const name = prefix ? `${prefix} / ${bin.name}` : bin.name;
      // Native IDs distinguish identically named bins; the label shows hierarchy.
      bins.push({ id: itemId(bin), name });
      if (bins.length > 1000) throw new Error('Project exceeds the supported limit of 1,000 bins.');
      await visit(bin, name, depth + 1);
    }
  }
  await visit(root, '', 0);
  return bins;
}
module.exports = { ProjectBins, enumerateBins };
