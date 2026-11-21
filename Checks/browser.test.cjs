const { test } = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");

test("browser handoff preserves signed URL query parameters and ignores other commands", () => {
  let click, install, menu, opened;
  const chrome = {
    runtime: { onInstalled: { addListener: fn => { install = fn; } } },
    contextMenus: {
      removeAll: callback => callback(),
      create: options => { menu = options; },
      onClicked: { addListener: fn => { click = fn; } }
    },
    tabs: { create: options => { opened = options.url; } }
  };
  vm.runInNewContext(fs.readFileSync(path.join(__dirname, "../Browser/worker.js"), "utf8"), { chrome, URL });
  install();
  const linkUrl = "https://example.org/game.zip?signature=a%2Bb&part=x%20y";
  click({ menuItemId: "unrelated", linkUrl });
  assert.equal(opened, undefined);
  click({ menuItemId: menu.id, linkUrl });
  const handoff = new URL(opened);
  assert.equal(handoff.protocol, "personaldownloader:");
  assert.equal(handoff.searchParams.get("url"), linkUrl);
});
