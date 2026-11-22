// zCast admin UI: "My Stations" editor. Plain ES2020, no dependencies.
// All edits are local until "Save"; the server stays authoritative for validation.
'use strict';

(() => {
  const API = 'api/';
  const MAX_NAME = 200;
  const MAX_URL = 2000;
  const MAX_IMPORT = 1024 * 1024;

  const state = {
    categories: [],      // [{ name, stations: [{ name, url }] }]
    savedJson: '[]',     // snapshot of the last loaded/saved list (for the dirty check)
    originalKeys: new Set(), // name + '\n' + url of stations at load time (receiver preset IDs)
    etag: null,
    file: null,
    loadBroken: false,   // GET returned 422 (file has a YAML error)
    sel: 0,              // selected category index
    renaming: false,
    editing: null,       // { cat, idx }
    tests: new Map(),    // url -> { pending: true } | test result
    busy: false,         // save / load / restore / import in progress
  };

  const $ = (id) => document.getElementById(id);

  // ---------- DOM helpers ----------

  function el(tag, props, ...children) {
    const node = document.createElement(tag);
    if (props) {
      for (const [key, value] of Object.entries(props)) {
        if (value === undefined || value === null || value === false) continue;
        if (key === 'class') node.className = value;
        else if (key === 'text') node.textContent = value;
        else if (key.startsWith('on')) node.addEventListener(key.slice(2), value);
        else if (key in node && typeof value !== 'string') node[key] = value;
        else node.setAttribute(key, value === true ? '' : value);
      }
    }
    for (const child of children.flat()) {
      if (child === null || child === undefined || child === false) continue;
      node.append(child instanceof Node ? child : document.createTextNode(String(child)));
    }
    return node;
  }

  function button(text, onclick, extra) {
    return el('button', Object.assign({ type: 'button', text, onclick }, extra || {}));
  }

  function toast(message, kind = 'ok', ms) {
    const node = el('div', { class: 'toast ' + kind, text: message, role: kind === 'error' ? 'alert' : 'status' });
    node.addEventListener('click', () => node.remove());
    $('toasts').append(node);
    setTimeout(() => node.remove(), ms || (kind === 'error' ? 9000 : 4000));
  }

  function showProblems(title, items, actions) {
    const box = $('problems');
    box.replaceChildren();
    box.append(el('strong', { text: title }));
    if (items && items.length) box.append(el('ul', null, items.map((t) => el('li', { text: t }))));
    if (actions && actions.length) box.append(el('div', { class: 'row-buttons' }, actions));
    box.append(el('div', { class: 'row-buttons' }, button('Dismiss', clearProblems)));
    box.hidden = false;
    box.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
  }

  function clearProblems() {
    $('problems').hidden = true;
    $('problems').replaceChildren();
  }

  // ---------- API ----------

  async function api(method, path, opts = {}) {
    const headers = Object.assign({ Accept: 'application/json' }, opts.headers || {});
    let body;
    if (method !== 'GET' && method !== 'HEAD') headers['X-Zcast-Admin'] = '1';
    if (opts.json !== undefined) {
      headers['Content-Type'] = 'application/json';
      body = JSON.stringify(opts.json);
    } else if (opts.text !== undefined) {
      headers['Content-Type'] = 'text/plain; charset=utf-8';
      body = opts.text;
    }
    let response;
    try {
      response = await fetch(API + path, { method, headers, body, credentials: 'same-origin', cache: 'no-store' });
    } catch (e) {
      const err = new Error('Network error – the server could not be reached. Your edits are kept.');
      err.network = true;
      throw err;
    }
    let data = null;
    const type = response.headers.get('Content-Type') || '';
    if (type.includes('json')) {
      try { data = await response.json(); } catch (e) { data = null; }
    } else {
      try { const t = await response.text(); data = t ? { title: t.slice(0, 300) } : null; } catch (e) { data = null; }
    }
    return { status: response.status, ok: response.ok, data, headers: response.headers };
  }

  function errorText(res, fallback) {
    const d = res.data || {};
    return d.title || d.detail || `${fallback} (HTTP ${res.status})`;
  }

  // ---------- Model helpers ----------

  const key = (s) => s.name + '\n' + s.url;
  const clean = (cats) => cats.map((c) => ({ name: c.name, stations: c.stations.map((s) => ({ name: s.name, url: s.url })) }));
  const isDirty = () => JSON.stringify(clean(state.categories)) !== state.savedJson;

  function setLoaded(categories, etag) {
    state.categories = clean(categories || []);
    state.savedJson = JSON.stringify(state.categories);
    state.originalKeys = new Set();
    for (const c of state.categories) for (const s of c.stations) state.originalKeys.add(key(s));
    state.etag = etag || null;
    state.editing = null;
    state.renaming = false;
    if (state.sel >= state.categories.length) state.sel = Math.max(0, state.categories.length - 1);
  }

  function changedPresetCount() {
    const now = new Set();
    for (const c of state.categories) for (const s of c.stations) now.add(key(s));
    let n = 0;
    for (const k of state.originalKeys) if (!now.has(k)) n++;
    return n;
  }

  // ---------- Validation (mirrors the server) ----------

  function categoryNameError(name, exceptIndex) {
    const n = name.trim();
    if (!n) return 'Category name is required.';
    if (n.length > MAX_NAME) return `Category name is longer than ${MAX_NAME} characters.`;
    if (/[/\\?#]/.test(n)) return 'Category name must not contain / \\ ? or #.';
    const lower = n.toLowerCase();
    if (state.categories.some((c, i) => i !== exceptIndex && c.name.trim().toLowerCase() === lower)) return `Category '${n}' already exists.`;
    return null;
  }

  function stationNameError(cat, name, exceptIndex) {
    const n = name.trim();
    if (!n) return 'Station name is required.';
    if (n.length > MAX_NAME) return `Station name is longer than ${MAX_NAME} characters.`;
    const lower = n.toLowerCase();
    if (cat && cat.stations.some((s, i) => i !== exceptIndex && s.name.trim().toLowerCase() === lower)) {
      return `A station named '${n}' already exists in '${cat.name}'.`;
    }
    return null;
  }

  function urlError(url) {
    const u = url.trim();
    if (!u) return 'Stream URL is required.';
    if (u.length > MAX_URL) return `URL is longer than ${MAX_URL} characters.`;
    let parsed;
    try { parsed = new URL(u); } catch (e) { return 'URL must be an absolute http:// or https:// address.'; }
    if (parsed.protocol !== 'http:' && parsed.protocol !== 'https:' || !/^https?:\/\//i.test(u)) {
      return 'URL must start with http:// or https://.';
    }
    return null;
  }

  function validateAll() {
    const errors = [];
    state.categories.forEach((c, ci) => {
      const ce = categoryNameError(c.name, ci);
      if (ce) errors.push(ce + (c.name.trim() ? '' : ` (category #${ci + 1})`));
      c.stations.forEach((s, si) => {
        const where = ` (${c.name || 'category #' + (ci + 1)} › ${s.name || 'station #' + (si + 1)})`;
        const ne = stationNameError(c, s.name, si);
        if (ne) errors.push(ne + where);
        const ue = urlError(s.url);
        if (ue) errors.push(ue + where);
      });
    });
    return [...new Set(errors)];
  }

  function uniqueName(cat, base) {
    const taken = new Set(cat.stations.map((s) => s.name.trim().toLowerCase()));
    let name = base.trim().slice(0, MAX_NAME) || 'Station';
    if (!taken.has(name.toLowerCase())) return name;
    for (let i = 2; ; i++) {
      const suffix = ` (${i})`;
      const candidate = name.slice(0, MAX_NAME - suffix.length) + suffix;
      if (!taken.has(candidate.toLowerCase())) return candidate;
    }
  }

  // ---------- Rendering ----------

  function render() {
    const dirty = isDirty();
    $('dirty').hidden = !dirty;
    $('btn-save').disabled = !dirty || state.busy;
    $('btn-discard').disabled = state.busy;
    $('btn-discard').textContent = dirty ? 'Discard' : 'Reload';
    $('import-label').classList.toggle('disabled', state.busy);
    $('import-file').disabled = state.busy;
    $('backups-refresh').disabled = state.busy;
    for (const b of document.querySelectorAll('.restore-btn')) b.disabled = state.busy;
    $('file-path').textContent = state.file || '–';
    renderCategories();
    renderStations();
    renderSearchTarget();
  }

  function renderCategories() {
    const list = $('cat-list');
    list.replaceChildren();
    if (!state.categories.length) {
      list.append(el('li', { class: 'empty', text: state.loadBroken ? 'Could not load the list.' : 'No categories yet.' }));
    }
    state.categories.forEach((c, i) => {
      list.append(el('li', null, el('button', {
        type: 'button',
        class: i === state.sel ? 'selected' : '',
        'aria-current': i === state.sel ? 'true' : null,
        onclick: () => { state.sel = i; state.renaming = false; state.editing = null; render(); },
      }, el('span', { text: c.name || '(no name)' }), el('span', { class: 'count', text: String(c.stations.length) }))));
    });
  }

  function renderStations() {
    const head = $('cat-head');
    const list = $('station-list');
    head.replaceChildren();
    list.replaceChildren();
    const cat = state.categories[state.sel];
    $('st-add').hidden = !cat;
    if (!cat) {
      head.append(el('p', { class: 'muted', text: 'Add a category to start adding stations.' }));
      return;
    }

    if (state.renaming) {
      const input = el('input', { type: 'text', value: cat.name, maxlength: String(MAX_NAME), 'aria-label': 'Category name' });
      const msg = el('div', { class: 'field-msg' });
      const form = el('form', { autocomplete: 'off', novalidate: true }, input, el('button', { type: 'submit', class: 'primary', text: 'OK' }),
        button('Cancel', () => { state.renaming = false; render(); }), msg);
      input.addEventListener('input', () => { msg.textContent = categoryNameError(input.value, state.sel) || ''; });
      form.addEventListener('submit', (e) => {
        e.preventDefault();
        const err = categoryNameError(input.value, state.sel);
        if (err) { msg.textContent = err; input.classList.add('invalid'); return; }
        cat.name = input.value.trim();
        state.renaming = false;
        render();
      });
      head.append(form);
      setTimeout(() => { input.focus(); input.select(); });
    } else {
      head.append(el('h2', { text: cat.name || '(no name)' }));
      head.append(el('div', { class: 'row-buttons' },
        button('Rename', () => { state.renaming = true; render(); }),
        button('↑', () => moveCategory(-1), { class: 'icon', title: 'Move category up', 'aria-label': 'Move category up', disabled: state.sel === 0 }),
        button('↓', () => moveCategory(1), { class: 'icon', title: 'Move category down', 'aria-label': 'Move category down', disabled: state.sel === state.categories.length - 1 }),
        button('Delete', deleteCategory, { class: 'danger' })));
    }

    if (!cat.stations.length) list.append(el('li', { class: 'empty', text: 'No stations in this category yet.' }));
    cat.stations.forEach((s, i) => {
      const editing = state.editing && state.editing.cat === state.sel && state.editing.idx === i;
      list.append(editing ? stationEditRow(cat, s, i) : stationRow(cat, s, i));
    });
  }

  function stationRow(cat, s, i) {
    const changed = !state.originalKeys.has(key(s));
    const info = el('div', { class: 'info' },
      el('div', { class: 'name' }, s.name, changed ? el('span', { class: 'changed', text: 'new/changed' }) : null),
      el('div', { class: 'url', text: s.url, title: s.url }));
    const testing = state.tests.get(s.url);
    const buttons = el('div', { class: 'row-buttons' },
      button(testing && testing.pending ? 'Testing…' : 'Test', () => runTest(s.url), { disabled: !!(testing && testing.pending) }),
      button('Edit', () => { state.editing = { cat: state.sel, idx: i }; render(); }),
      button('↑', () => moveStation(i, -1), { class: 'icon', title: 'Move up', 'aria-label': 'Move up', disabled: i === 0 }),
      button('↓', () => moveStation(i, 1), { class: 'icon', title: 'Move down', 'aria-label': 'Move down', disabled: i === cat.stations.length - 1 }),
      button('✕', () => deleteStation(i), { class: 'icon danger', title: 'Delete station', 'aria-label': 'Delete station' }));
    const useUrl = (u) => {
      const err = urlError(u);
      if (err) { toast(err, 'error'); return; }
      s.url = u.trim();
      render();
      toast(`'${s.name}' now uses ${s.url} (unsaved – click Save).`);
    };
    return el('li', null, el('div', { class: 'station' }, info, buttons), testResultNode(s.url, useUrl));
  }

  function stationEditRow(cat, s, i) {
    const name = el('input', { type: 'text', value: s.name, maxlength: String(MAX_NAME) });
    const url = el('input', { type: 'url', value: s.url, maxlength: String(MAX_URL) });
    const target = el('select', null, state.categories.map((c, ci) => el('option', { value: String(ci), text: c.name, selected: ci === state.sel })));
    const msg = el('div', { class: 'field-msg' });
    const result = el('div', { class: 'test-result' });
    const check = () => {
      const ci = Number(target.value);
      const tcat = state.categories[ci];
      const err = stationNameError(tcat, name.value, ci === state.sel ? i : -1) || urlError(url.value);
      msg.textContent = err || '';
      return err;
    };
    name.addEventListener('input', check);
    url.addEventListener('input', check);
    target.addEventListener('change', check);
    const form = el('form', { class: 'edit-form', autocomplete: 'off', novalidate: true },
      el('label', null, 'Name', name),
      el('label', null, 'Stream URL', url),
      el('label', null, 'Category', target),
      el('div', { class: 'row-buttons' },
        el('button', { type: 'submit', class: 'primary', text: 'Apply' }),
        button('Cancel', () => { state.editing = null; render(); }),
        button('Test', (e) => runTestInto(url.value, result, e.currentTarget, (u) => {
          url.value = u;
          check();
          result.replaceChildren(el('span', { class: 'ok', text: 'URL replaced. ' }), 'Click Apply to keep it.');
        }))),
      msg, result);
    form.addEventListener('submit', (e) => {
      e.preventDefault();
      if (check()) return;
      const ci = Number(target.value);
      const updated = { name: name.value.trim(), url: url.value.trim() };
      if (ci === state.sel) {
        cat.stations[i] = updated;
      } else {
        cat.stations.splice(i, 1);
        state.categories[ci].stations.push(updated);
        toast(`Moved '${updated.name}' to '${state.categories[ci].name}'.`);
      }
      state.editing = null;
      render();
    });
    setTimeout(() => name.focus());
    return el('li', null, form);
  }

  function testResultNode(url, onUseUrl) {
    const node = el('div', { class: 'test-result' });
    const r = state.tests.get(url);
    if (r) fillTestResult(node, r, onUseUrl);
    return node;
  }

  // onUseUrl(url) is called by the "Use http:// URL" button when the test suggests an http:// variant.
  function fillTestResult(node, r, onUseUrl) {
    node.replaceChildren();
    if (r.pending) {
      node.append(el('span', { class: 'spinner' }), 'Testing stream…');
      return;
    }
    if (r.error) {
      node.append(el('span', { class: 'bad', text: '✗ ' }), r.error);
      return;
    }
    const parts = [];
    if (r.contentType) parts.push(r.contentType);
    if (r.name) parts.push(`“${r.name}”`);
    if (r.status && !r.ok) parts.push(`HTTP ${r.status}`);
    node.append(el('span', { class: r.ok ? 'ok' : 'bad', text: r.ok ? '✓ Plays' : '✗ Problem' }));
    if (parts.length) node.append(' – ' + parts.join(' · '));
    if (r.finalUrl && r.finalUrl !== r.url) node.append(el('div', { class: 'muted', text: 'Redirects to ' + r.finalUrl }));
    if (r.warnings && r.warnings.length) node.append(el('ul', null, r.warnings.map((w) => el('li', { text: w }))));
    if (r.suggestedUrl && onUseUrl) {
      node.append(el('div', { class: 'suggest' },
        button('Use http:// URL', () => onUseUrl(r.suggestedUrl), { class: 'primary' }),
        el('span', { class: 'muted', text: r.suggestedUrl })));
    }
  }

  function renderSearchTarget() {
    const cat = state.categories[state.sel];
    $('search-target').textContent = cat ? `“Add” puts the station into: ${cat.name}` : 'Select or create a category first to add results.';
    for (const b of document.querySelectorAll('.search-add')) b.disabled = !cat;
  }

  // ---------- Category / station actions ----------

  function moveCategory(delta) {
    const i = state.sel, j = i + delta;
    if (j < 0 || j >= state.categories.length) return;
    const list = state.categories;
    [list[i], list[j]] = [list[j], list[i]];
    state.sel = j;
    render();
  }

  function deleteCategory() {
    const cat = state.categories[state.sel];
    if (!cat) return;
    if (cat.stations.length && !confirm(`Delete category '${cat.name}' and its ${cat.stations.length} station(s)?`)) return;
    state.categories.splice(state.sel, 1);
    state.sel = Math.max(0, Math.min(state.sel, state.categories.length - 1));
    state.renaming = false;
    state.editing = null;
    render();
  }

  function moveStation(i, delta) {
    const list = state.categories[state.sel].stations;
    const j = i + delta;
    if (j < 0 || j >= list.length) return;
    [list[i], list[j]] = [list[j], list[i]];
    state.editing = null;
    render();
  }

  function deleteStation(i) {
    const cat = state.categories[state.sel];
    const s = cat.stations[i];
    if (!confirm(`Delete station '${s.name}'?`)) return;
    cat.stations.splice(i, 1);
    state.editing = null;
    render();
  }

  // ---------- Stream test ----------

  async function testStream(url) {
    try {
      const res = await api('POST', 'test-stream', { json: { url: url.trim() } });
      if (!res.ok) return { error: errorText(res, 'Test failed') };
      return Object.assign({ url }, res.data);
    } catch (e) {
      return { error: e.message };
    }
  }

  async function runTest(url) {
    state.tests.set(url, { pending: true });
    renderStations();
    const r = await testStream(url);
    state.tests.set(url, r);
    renderStations();
  }

  async function runTestInto(url, node, btn, onUseUrl) {
    if (urlError(url)) { fillTestResult(node, { error: urlError(url) }); return; }
    if (btn) btn.disabled = true;
    fillTestResult(node, { pending: true });
    const r = await testStream(url);
    if (btn) btn.disabled = false;
    if (r.url) state.tests.set(r.url.trim(), r);
    fillTestResult(node, r, onUseUrl);
  }

  // ---------- Load / save ----------

  async function load(quiet) {
    state.busy = true;
    render();
    try {
      const res = await api('GET', 'stations');
      if (res.ok) {
        state.loadBroken = false;
        state.file = res.data.file || null;
        setLoaded(res.data.categories, res.data.eTag || res.headers.get('ETag'));
        clearProblems();
        if (!quiet) toast('Station list loaded.');
      } else if (res.status === 422) {
        state.loadBroken = true;
        const d = res.data || {};
        // The server sends the broken file's ETag, so a corrected list can replace it directly.
        setLoaded([], res.headers.get('ETag') || d.etag || null);
        showProblems(d.title || 'stations.yml has a format error', [d.detail || ''].filter(Boolean));
      } else {
        showProblems(errorText(res, 'Could not load the station list'));
      }
    } catch (e) {
      showProblems(e.message);
    } finally {
      state.busy = false;
      render();
    }
  }

  async function save() {
    if (!isDirty() || state.busy) return;
    if (state.editing || state.renaming) {
      toast('Finish or cancel the open edit form first.', 'warn');
      return;
    }
    const errors = validateAll();
    if (errors.length) {
      showProblems('Please fix these problems before saving', errors);
      return;
    }
    const changed = changedPresetCount();
    if (changed > 0 && !confirm(`${changed} station(s) changed name or URL – presets saved on the receiver for them will stop working. Save anyway?`)) return;
    if (state.loadBroken && state.etag
        && !confirm('The stations.yml on the server has a format error. Replace it with the list in the editor? (A backup is kept.)')) return;
    await put(state.etag || '"*"');
  }

  async function put(ifMatch) {
    state.busy = true;
    render();
    const payload = clean(state.categories).map((c) => ({
      name: c.name.trim(),
      stations: c.stations.map((s) => ({ name: s.name.trim(), url: s.url.trim() })),
    }));
    try {
      const res = await api('PUT', 'stations', { json: { categories: payload }, headers: { 'If-Match': ifMatch } });
      if (res.ok) {
        state.loadBroken = false;
        setLoaded(payload, (res.data && (res.data.etag || res.data.eTag)) || res.headers.get('ETag'));
        clearProblems();
        toast('Saved. The receiver now sees the new list.');
        if (!state.file) load(true);
      } else if (res.status === 400) {
        const d = res.data || {};
        showProblems(d.title || 'The server rejected the list', d.errors || []);
      } else if (res.status === 412) {
        const d = res.data || {};
        if (state.loadBroken && d.etag && !state.etag) {
          // Fallback if the 422 carried no ETag: confirm overwriting the broken file.
          state.busy = false;
          if (confirm('The stations.yml on the server has a format error. Replace it with the list in the editor? (A backup is kept.)')) {
            await put(d.etag);
          }
          return;
        }
        showProblems('Someone else changed the station list since you loaded it.',
          ['Your changes were not saved. Reload to get the current list (your edits will be lost), or export/copy what you need first.'],
          [button('Reload (discard my changes)', () => { clearProblems(); load(); }, { class: 'primary' })]);
      } else {
        showProblems(errorText(res, 'Saving failed'), (res.data && res.data.errors) || []);
      }
    } catch (e) {
      showProblems(e.message);
    } finally {
      state.busy = false;
      render();
    }
  }

  function discard() {
    if (isDirty() && !confirm('Discard all unsaved changes and reload the list from the server?')) return;
    state.tests.clear();
    load();
  }

  // ---------- Import / backups ----------

  async function importFile(file) {
    if (!file) return;
    if (file.size > MAX_IMPORT) { toast('File is too large (max. 1 MB).', 'error'); return; }
    if (isDirty() && !confirm('Importing replaces the list in the editor, including your unsaved changes. Continue?')) return;
    state.busy = true;
    render();
    try {
      const text = await file.text();
      const res = await api('POST', 'parse', { text });
      if (res.ok) {
        state.categories = clean(res.data.categories || []);
        state.sel = 0;
        state.editing = null;
        state.renaming = false;
        const errors = res.data.errors || [];
        if (errors.length) showProblems(`Imported '${file.name}' with problems – fix them before saving`, errors);
        else clearProblems();
        toast(`Imported '${file.name}'. Review and click Save to apply.`, errors.length ? 'warn' : 'ok');
      } else {
        const d = res.data || {};
        showProblems(d.title || `Import failed (HTTP ${res.status})`, d.errors || []);
      }
    } catch (e) {
      showProblems(e.message);
    } finally {
      state.busy = false;
      $('import-file').value = '';
      render();
    }
  }

  function formatSize(bytes) {
    if (bytes < 1024) return `${bytes} B`;
    return `${(bytes / 1024).toFixed(bytes < 10240 ? 1 : 0)} KB`;
  }

  async function loadBackups() {
    const list = $('backup-list');
    list.replaceChildren(el('li', null, el('span', { class: 'spinner' }), 'Loading…'));
    try {
      const res = await api('GET', 'backups');
      list.replaceChildren();
      if (!res.ok) { list.append(el('li', { class: 'empty', text: errorText(res, 'Could not load backups') })); return; }
      if (!res.data.length) { list.append(el('li', { class: 'empty', text: 'No backups yet. One is made every time you save.' })); return; }
      for (const b of res.data) {
        const date = new Date(b.createdUtc);
        const when = isNaN(date) ? b.id : date.toLocaleString();
        list.append(el('li', null,
          el('div', { class: 'info' }, el('div', { text: when }), el('div', { class: 'muted small', text: `${formatSize(b.size)} · ${b.id}` })),
          button('Restore', () => restoreBackup(b.id, when), { class: 'restore-btn', disabled: state.busy })));
      }
    } catch (e) {
      list.replaceChildren(el('li', { class: 'empty', text: e.message }));
    }
  }

  async function restoreBackup(id, when) {
    if (state.busy) return;
    if (isDirty() && !confirm('You have unsaved changes. Restoring a backup discards them. Continue?')) return;
    if (!confirm(`Restore the backup from ${when}? It replaces the current list on the server right away (the current version is backed up first).`)) return;
    state.busy = true;
    render();
    try {
      const res = await api('POST', `backups/${encodeURIComponent(id)}/restore`);
      if (res.ok) {
        toast(`Backup from ${when} restored.`);
        state.busy = false;
        state.tests.clear();
        await load(true);
        loadBackups();
      } else {
        const d = res.data || {};
        showProblems(d.title || errorText(res, 'Restore failed'), d.errors || []);
      }
    } catch (e) {
      showProblems(e.message);
    } finally {
      state.busy = false;
      render();
    }
  }

  // ---------- Search ----------

  const FILTER_STORE_KEY = 'zcast.admin.searchFilters';
  const FILTER_SELECTS = { country: 'sf-country', language: 'sf-language', tag: 'sf-tag', codec: 'sf-codec' };
  const search = { filters: null, loadingFilters: false, params: null, offset: 0, count: 0, hasMore: false, busy: false };

  const capitalize = (s) => (s ? s.charAt(0).toUpperCase() + s.slice(1) : s);

  function readFilters() {
    return {
      name: $('sf-name').value.trim(),
      country: $('sf-country').value,
      language: $('sf-language').value,
      tag: $('sf-tag').value,
      codec: $('sf-codec').value,
      bitrateMin: $('sf-bitrate').value,
      order: $('sf-order').value || 'votes',
      httpOnly: $('sf-http').checked,
    };
  }

  function storedFilters() {
    try {
      const raw = localStorage.getItem(FILTER_STORE_KEY);
      const parsed = raw ? JSON.parse(raw) : null;
      return parsed && typeof parsed === 'object' ? parsed : {};
    } catch (e) {
      return {};
    }
  }

  function storeFilters(f) {
    try { localStorage.setItem(FILTER_STORE_KEY, JSON.stringify(f)); } catch (e) { /* storage unavailable */ }
  }

  function setSelect(select, value) {
    value = value == null ? '' : String(value);
    if (value && ![...select.options].some((o) => o.value === value)) {
      // Keep a remembered value usable even if the filter lists are not (yet) loaded.
      select.append(el('option', { value, text: capitalize(value) }));
    }
    select.value = value;
  }

  function applyFilters(f) {
    $('sf-name').value = f.name || '';
    for (const [k, id] of Object.entries(FILTER_SELECTS)) setSelect($(id), f[k]);
    setSelect($('sf-bitrate'), f.bitrateMin);
    $('sf-order').value = ['votes', 'popular', 'name', 'bitrate'].includes(f.order) ? f.order : 'votes';
    $('sf-http').checked = f.httpOnly !== false;
  }

  function fillFilterSelect(select, items, display) {
    const current = select.value;
    select.replaceChildren(el('option', { value: '', text: 'Any' }));
    for (const item of items || []) {
      const label = display(item.label || item.value);
      select.append(el('option', { value: item.value, text: item.count != null ? `${label} (${item.count})` : label }));
    }
    setSelect(select, current);
  }

  async function loadFilters() {
    if (search.filters || search.loadingFilters) return;
    search.loadingFilters = true;
    const status = $('filter-status');
    status.replaceChildren(el('span', { class: 'spinner' }), 'Loading filters…');
    try {
      const res = await api('GET', 'search/filters');
      if (!res.ok || !res.data) throw new Error(errorText(res, 'Could not load the filter lists'));
      search.filters = res.data;
      fillFilterSelect($('sf-country'), res.data.countries, (s) => s);
      fillFilterSelect($('sf-language'), res.data.languages, capitalize);
      fillFilterSelect($('sf-tag'), res.data.tags, capitalize);
      fillFilterSelect($('sf-codec'), res.data.codecs, (s) => s);
      status.replaceChildren();
    } catch (e) {
      status.replaceChildren(el('span', { class: 'bad', text: e.message + ' You can still search by name. ' }),
        button('Retry', () => loadFilters()));
    } finally {
      search.loadingFilters = false;
    }
  }

  function setSearchBusy(busy) {
    search.busy = busy;
    $('search-btn').disabled = busy;
    $('search-more').disabled = busy;
    $('search-reset').disabled = busy;
  }

  function updateSearchCount() {
    $('search-count').textContent = search.params
      ? `${search.count} station(s) shown${search.hasMore ? ' – more available' : ''}`
      : '';
    $('search-more').hidden = !search.hasMore;
  }

  async function runSearch() {
    const f = readFilters();
    const msg = $('search-msg');
    msg.textContent = '';
    if (f.name && f.name.length < 2) { msg.textContent = 'Enter at least 2 characters of the name.'; return; }
    if (!f.name && !f.country && !f.language && !f.tag) {
      msg.textContent = 'Enter a name (min. 2 characters) or choose a country, language or genre.';
      return;
    }
    storeFilters(f);
    const params = new URLSearchParams();
    for (const k of ['name', 'country', 'language', 'tag', 'codec', 'bitrateMin']) if (f[k]) params.set(k, f[k]);
    params.set('order', f.order);
    params.set('httpOnly', f.httpOnly ? 'true' : 'false');
    search.params = params;
    search.offset = 0;
    search.count = 0;
    search.hasMore = false;
    $('search-results').replaceChildren();
    await fetchSearchPage();
  }

  async function fetchSearchPage() {
    if (!search.params || search.busy) return;
    const list = $('search-results');
    const params = new URLSearchParams(search.params);
    params.set('offset', String(search.offset));
    setSearchBusy(true);
    const loading = el('li', null, el('span', { class: 'spinner' }), 'Searching…');
    list.append(loading);
    try {
      const res = await api('GET', 'search?' + params.toString());
      loading.remove();
      if (!res.ok) { list.append(el('li', { class: 'empty', text: errorText(res, 'Search failed') })); search.hasMore = false; return; }
      const results = (res.data && res.data.results) || [];
      for (const r of results) list.append(searchRow(r));
      search.count += results.length;
      search.offset = (res.data.offset || search.offset) + results.length;
      search.hasMore = !!res.data.hasMore && results.length > 0;
      if (!search.count) list.append(el('li', { class: 'empty', text: 'No stations found. Try fewer filters.' }));
      renderSearchTarget();
    } catch (e) {
      loading.remove();
      list.append(el('li', { class: 'empty', text: e.message }));
    } finally {
      setSearchBusy(false);
      updateSearchCount();
    }
  }

  function resetSearch() {
    applyFilters({ httpOnly: true, order: 'votes' });
    storeFilters(readFilters());
    search.params = null;
    search.count = 0;
    search.hasMore = false;
    $('search-msg').textContent = '';
    $('search-results').replaceChildren();
    updateSearchCount();
  }

  function searchRow(r) {
    const meta = [];
    const codec = [r.codec, r.bitrate ? `${r.bitrate} kbps` : ''].filter(Boolean).join(' ');
    if (codec) meta.push(codec);
    if (r.country) meta.push(r.country);
    if (r.language) meta.push(capitalize(r.language));
    if (r.votes) meta.push(`${r.votes} votes`);
    if (r.tags) meta.push(r.tags);
    const result = el('div', { class: 'test-result' });
    const urlNode = el('div', { class: 'meta url', text: r.url, title: r.url });
    const useUrl = (u) => {
      r.url = u;
      urlNode.textContent = u;
      urlNode.title = u;
      result.replaceChildren(el('span', { class: 'ok', text: 'URL changed to http://. ' }), '“Add” will use it.');
    };
    return el('li', null,
      el('div', { class: 'info' },
        el('div', { class: 'name', text: r.name || '(no name)' }),
        el('div', { class: 'meta', text: meta.join(' · ') }),
        urlNode,
        result),
      el('div', { class: 'row-buttons' },
        button('Test', (e) => runTestInto(r.url || '', result, e.currentTarget, useUrl)),
        button('Add', () => addFromSearch(r), { class: 'search-add primary', disabled: !state.categories[state.sel] })));
  }

  function addFromSearch(r) {
    const cat = state.categories[state.sel];
    if (!cat) { toast('Select or create a category first.', 'warn'); return; }
    const url = (r.url || '').trim();
    const ue = urlError(url);
    if (ue) { toast(ue, 'error'); return; }
    if (cat.stations.some((s) => s.url.trim() === url)) {
      if (!confirm(`'${cat.name}' already has a station with this URL. Add it again?`)) return;
    }
    const name = uniqueName(cat, r.name || 'Station');
    cat.stations.push({ name, url });
    render();
    toast(name !== (r.name || '').trim() ? `Added as '${name}' (name already taken) to '${cat.name}'.` : `Added '${name}' to '${cat.name}'.`);
  }

  // ---------- Wiring ----------

  function init() {
    $('btn-save').addEventListener('click', save);
    $('btn-discard').addEventListener('click', discard);

    $('cat-add-name').addEventListener('input', (e) => {
      $('cat-add-msg').textContent = e.target.value.trim() ? (categoryNameError(e.target.value, -1) || '') : '';
    });
    $('cat-add').addEventListener('submit', (e) => {
      e.preventDefault();
      const input = $('cat-add-name');
      const err = categoryNameError(input.value, -1);
      if (err) { $('cat-add-msg').textContent = err; return; }
      state.categories.push({ name: input.value.trim(), stations: [] });
      state.sel = state.categories.length - 1;
      state.editing = null;
      state.renaming = false;
      input.value = '';
      $('cat-add-msg').textContent = '';
      render();
      $('st-add-name').focus();
    });

    const addCheck = () => {
      const name = $('st-add-name').value, url = $('st-add-url').value;
      if (!name.trim() && !url.trim()) { $('st-add-msg').textContent = ''; return; }
      $('st-add-msg').textContent = (name.trim() ? stationNameError(state.categories[state.sel], name, -1) : null)
        || (url.trim() ? urlError(url) : null) || '';
    };
    $('st-add-name').addEventListener('input', addCheck);
    $('st-add-url').addEventListener('input', addCheck);
    $('st-add-test').addEventListener('click', (e) => runTestInto($('st-add-url').value, $('st-add-test-result'), e.currentTarget, (u) => {
      $('st-add-url').value = u;
      addCheck();
      $('st-add-test-result').replaceChildren(el('span', { class: 'ok', text: 'URL replaced with the http:// version.' }));
    }));
    $('st-add').addEventListener('submit', (e) => {
      e.preventDefault();
      const cat = state.categories[state.sel];
      if (!cat) return;
      const name = $('st-add-name').value, url = $('st-add-url').value;
      const err = stationNameError(cat, name, -1) || urlError(url);
      if (err) { $('st-add-msg').textContent = err; return; }
      cat.stations.push({ name: name.trim(), url: url.trim() });
      $('st-add-name').value = '';
      $('st-add-url').value = '';
      $('st-add-msg').textContent = '';
      $('st-add-test-result').replaceChildren();
      render();
      $('st-add-name').focus();
    });

    $('search-form').addEventListener('submit', (e) => { e.preventDefault(); runSearch(); });
    $('search-more').addEventListener('click', fetchSearchPage);
    $('search-reset').addEventListener('click', resetSearch);
    $('search-panel').addEventListener('toggle', (e) => { if (e.target.open) loadFilters(); });
    applyFilters(storedFilters());

    $('import-file').addEventListener('change', (e) => importFile(e.target.files && e.target.files[0]));
    $('backups-refresh').addEventListener('click', loadBackups);
    $('file-panel').addEventListener('toggle', (e) => { if (e.target.open) loadBackups(); });
    $('export-link').addEventListener('click', (e) => {
      if (isDirty()) toast('Export downloads the saved file on the server, without your unsaved changes.', 'warn', 6000);
    });

    window.addEventListener('beforeunload', (e) => {
      if (isDirty()) { e.preventDefault(); e.returnValue = ''; }
    });

    load(true);
  }

  init();
})();
