chrome.runtime.onInstalled.addListener(() => {
  chrome.contextMenus.removeAll(() => chrome.contextMenus.create({
    id: "send-link",
    title: "Send link to LinkFoundry",
    contexts: ["link"],
    targetUrlPatterns: ["http://*/*", "https://*/*"]
  }));
});

chrome.contextMenus.onClicked.addListener((item) => {
  if (item.menuItemId !== "send-link" || !item.linkUrl) return;
  const url = new URL(item.linkUrl);
  if (!["http:", "https:"].includes(url.protocol)) return;
  chrome.tabs.create({ url: "personaldownloader://add/?url=" + encodeURIComponent(url.href) });
});
