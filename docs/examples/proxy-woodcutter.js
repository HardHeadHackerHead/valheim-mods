// ==Script==
// name:    Proxy Woodcutter
// version: 1.0
// author:  Quad
// about:   The Terminal's Proxy walks to the nearest tree, chops it, collects the wood, and puts it in a chest. Put an axe in the Proxy's bag first.
// needs:   proxy, messages
// api:     1
// ==/Script==

let state = "idle";

every(3, () => {
  if (proxy.busy) return;                               // a player is driving it
  if (state === "idle") {
    if (!proxy.has("AxeStone") && !proxy.has("AxeFlint") && !proxy.has("AxeBronze")) { proxy.say("I need an axe."); return; }
    proxy.equip("hand", proxy.has("AxeBronze") ? "AxeBronze" : proxy.has("AxeStone") ? "AxeStone" : "AxeFlint");
    const t = proxy.nearest("tree", 40);
    if (t && proxy.walkTo(t.pos)) state = "toTree";
  } else if (state === "toTree") {
    if (proxy.arrived()) state = "chopping";
  } else if (state === "chopping") {
    const t = proxy.nearest("tree", 6);
    if (t) proxy.attack(t); else state = "collect";
  } else if (state === "collect") {
    const log = proxy.nearest("pickup", 12);
    if (log) proxy.use(log); else state = "toChest";
  } else if (state === "toChest") {
    const chest = proxy.nearest("chest", 60);
    if (chest && proxy.walkTo(chest.pos)) state = "atChest"; else state = "idle";
  } else if (state === "atChest") {
    if (proxy.arrived()) { proxy.deposit(proxy.nearest("chest", 6), { only: ["Wood"] }); state = "idle"; }
  }
});

on("proxyEvent", e => { if (e.kind === "taken" || e.kind === "blocked") state = "idle"; });
