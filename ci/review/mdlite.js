// A Markdown subset for the review's static sections: headings (h3), paragraphs, nested lists, tables, bold, code.
const esc = s => String(s).replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;");
const SEV = ["blocker", "major", "minor", "note"];
let GLOSSARY = {};
const setGlossary = g => { GLOSSARY = g || {}; };
// {{Term}} becomes a tappable term whose definition opens in place; an unknown term stays plain text.
const term = key => GLOSSARY[key]
  ? `<button type="button" class="ref" aria-expanded="false">${esc(key)}</button><span class="gloss" hidden><b>${esc(key)}</b> · ${inline(GLOSSARY[key])}</span>`
  : esc(key);
function inline(t) {
  const codes = [];
  t = String(t).replace(/`([^`]+)`/g, (_, c) => { codes.push(c); return "\u0000" + (codes.length - 1) + "\u0000"; });
  t = esc(t);
  t = t.replace(/\*\*([^*]+)\*\*/g, "<strong>$1</strong>");
  t = t.replace(/\{\{([^{}]+)\}\}/g, (_, k) => term(k.replace(/&lt;/g, "<").replace(/&gt;/g, ">").replace(/&quot;/g, "\"").replace(/&amp;/g, "&")));
  t = t.replace(/\u0000(\d+)\u0000/g, (_, i) => `<code>${esc(codes[+i])}</code>`);
  return t;
}
function cell(t, header) {
  const c = t.trim();
  if (!header && SEV.includes(c.toLowerCase())) return `<span class="sev sev-${c.toLowerCase()}">${c}</span>`;
  return inline(c);
}
function render(md) {
  const lines = md.replace(/\r\n/g, "\n").split("\n");
  let html = "", para = [], stack = [], i = 0;
  const flush = () => { if (para.length) { html += `<p>${inline(para.join(" "))}</p>`; para = []; } };
  const close = (to = -1) => { while (stack.length && stack[stack.length - 1].indent > to) { html += `</li></${stack.pop().type}>`; } };
  while (i < lines.length) {
    const line = lines[i], t = line.trim();
    if (!t) { flush(); i++; continue; }
    const h = /^#{3,4} (.*)$/.exec(t);
    if (h) { flush(); close(); html += `<h3>${inline(h[1])}</h3>`; i++; continue; }
    if (t.startsWith("|")) {
      flush(); close();
      const rows = [];
      while (i < lines.length && lines[i].trim().startsWith("|")) rows.push(lines[i++].trim());
      const split = r => r.replace(/^\||\|$/g, "").split("|");
      html += `<div class="table"><table><thead><tr>${split(rows[0]).map(c => `<th>${cell(c, true)}</th>`).join("")}</tr></thead><tbody>`
        + rows.slice(2).map(r => `<tr>${split(r).map(c => `<td>${cell(c, false)}</td>`).join("")}</tr>`).join("") + `</tbody></table></div>`;
      continue;
    }
    const li = /^(\s*)([-*]|\d+\.) (.*)$/.exec(line);
    if (li) {
      flush();
      const indent = li[1].length, type = /\d/.test(li[2]) ? "ol" : "ul", top = stack[stack.length - 1];
      if (!top || indent > top.indent) { html += `<${type}><li>`; stack.push({ type, indent }); }
      else { close(indent); const cur = stack[stack.length - 1]; if (cur && cur.indent === indent) html += `</li><li>`; else { html += `<${type}><li>`; stack.push({ type, indent }); } }
      html += inline(li[3]); i++; continue;
    }
    if (stack.length && /^\s{2,}\S/.test(line)) { html += `<p class="cont">${inline(t)}</p>`; i++; continue; }
    close(); para.push(t); i++;
  }
  flush(); close();
  return html;
}
function section(md, heading) {
  const start = md.indexOf("\n## " + heading);
  if (start < 0) return "";
  const from = md.indexOf("\n", start + 1);
  const next = md.indexOf("\n## ", from);
  return md.slice(from + 1, next < 0 ? undefined : next);
}
module.exports = { esc, inline, render, section, setGlossary };
