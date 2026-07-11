const categories = {
  movement: { label: "移动与避障", feature: "纯 C# 固定 Tick 模拟负责单位加减速、局部避障、碰撞推挤和到达判定；Godot 只提供输入、绘制与静态路径查询，使移动逻辑可重复、可独立验证。" },
  destination: { label: "编队落位", feature: "目标槽位系统为群组分配不重叠落点，并通过局部重匹配、主动让路、外圈等待与 Overflow 恢复，处理大编队在狭小目标区的收敛。" },
  choke: { label: "路径与狭口", feature: "Portal 高层图、狭口状态机和双向通行租约共同决定群组路线、车道与放行批次，避免单位在窄口互锁，同时维持确定性的调度顺序。" },
  dynamic: { label: "动态地图", feature: "动态建筑写入占用网格并推进导航版本；系统只让受影响的路径失效，同组单位共享重规划，并在建筑移除或 Portal 关闭后安全恢复。" },
  clearance: { label: "净空与建筑", feature: "Small / Medium / Large 移动等级贯穿网格、Portal、路径复验和建筑放置；版本化 Resource 与离线 Bake 把编辑数据转换成纯 C# 不可变快照。" },
  combat: { label: "战斗移动", feature: "AttackMove、索敌、追击与原路线恢复由独立状态机驱动；近战接触槽和远程攻击环避免同一落点争抢，死亡清理不破坏稳定单位 ID。" },
  operation: { label: "操作与命令", feature: "操作层把选择、编组、SmartCommand 和每单位 Shift 队列转换成稳定业务命令；相同 Tick 的队列项会重新批量寻路，并保持命令版本隔离。" },
  replay: { label: "确定性回放", feature: "规范命令日志、稳定状态 Hash、Replay Package、Checkpoint 与热快照组成分层回放方案，可重建世界、定位首次分歧，也可跳过早期 Tick 直接恢复运行态。" },
  interface: { label: "选择与界面", feature: "选择过滤、相机和 Minimap 使用纯数据快照与交互意图连接 Godot Control；坐标转换和业务命令与表现层解耦，便于换皮并保持可测试性。" }
};

const caseRows = `
single-unit|单单位基础到达|确认单个单位能沿路径抵达目标，位置保持有限且不会越出世界边界。
open-field|开放场大编队移动|让 48 个单位横穿开放区域，观察群组寻路、避让与最终到达。
dense-formation|密集编队展开|用高密度初始站位验证单位能解除重叠、形成稳定间距并继续前进。
opposing-streams|对向人流避让|两组单位迎面交汇，验证候选速度与避让侧记忆不会造成持续堵塞。
crossing-streams|交叉人流避让|两股单位以交叉轨迹穿越，检查局部避障是否维持连续流动。
command-replace|移动命令替换|单位行进中下发新目标，验证旧命令和旧路径立即失效。
rapid-reissue|高频重发命令|连续快速改变目标，确认命令版本隔离能阻止过期寻路结果回写。
stop-command|移动中停止|对正在行进的单位执行 Stop，确认它们制动并回到 Idle。
hold-command|移动中原地驻守|执行 Hold Position，确认单位停止并保持驻守状态。
mixed-radii|混合半径编队|让不同碰撞半径的单位共同移动，验证间距和避障使用各自尺寸。
boundary-target|边界目标修正|把目标设在地图边缘，确认落点被安全夹紧且单位不越界。
large-group-192|192 单位压力场景|用 192 单位验证大群组移动的稳定性、到达率与无异常数值。
destination-convergence|临时阻挡后的终点收敛|目标区存在暂时停留的编队成员时，验证让路与重新落位最终能收敛。
destination-outer-ring|外圈先到的终点释放|让外圈槽位先被占据，确认内部预留会等待并在条件合适时进入。
destination-overtake|快单位终点前超越|后排高速单位追上前排，验证局部重匹配减少交叉并保持落位秩序。
destination-corner-mixed|角落混合半径落位|在受边界夹紧的角落目标处，让不同尺寸单位完成无重叠收敛。
shared-target-reservations|跨波次共享目标预留|先后两批单位奔向同一目标，确认不同命令波次不会拿到重复槽位。
portal-choke|正向单通道穿越|大群组通过单一 Portal 狭口，检查入口排序、车道与出口疏散。
reverse-choke|反向单通道穿越|从反方向通过相同狭口，确认导航和狭口状态机没有方向偏置。
bidirectional-choke-balanced|平衡双向狭口|数量相近的两股单位争用狭口，验证方向租约与排空切换。
bidirectional-choke-asymmetric|非对称双向狭口|两侧人数明显不同时，确认批次调度兼顾吞吐且不会饿死少数侧。
bidirectional-choke-waves|连续波次双向狭口|多批单位持续抵达两端，验证等待统计、方向切换和防饥饿逻辑。
hold-blocked-choke|驻守单位堵塞狭口|用 Hold 单位占住通道后再释放，确认双端会暂停放行并安全恢复。
temporary-blocker-recovery|临时包围恢复|移动单位被其他单位短暂围住，验证有限恢复阶梯能在解围后继续。
unreachable-recovery-limit|永久不可达停止重试|用贯穿地图的障碍隔绝目标，确认恢复次数有上限并进入 Unreachable。
dynamic-building-detour|动态建筑触发绕行|在活动路径上放置建筑，确认单位发现占用并改走可通行路线。
dynamic-building-remove|移除建筑后恢复|先用建筑阻断路径再移除，确认导航版本更新后单位恢复前进。
dynamic-portal-reroute|活动 Portal 关闭改道|关闭当前高层路线所用 Portal，验证路线规划器选择备用通路。
dynamic-local-invalidation|局部路径精准失效|建筑只横切其中一组路线，确认无关单位不会被迫重新寻路。
dynamic-group-reroute|群组共享动态改道|大编队的活动 Portal 失效后，确认同组只做一次高层重规划并共享结果。
navigation-resource-runtime|导航 Resource 驱动运行时|从 Godot Resource 加载世界、障碍、Portal、Edge 与 Choke，验证转换后的快照可直接导航。
clearance-portal-choice|按单位尺寸选择 Portal|小单位走窄门、大单位绕行宽门，确认通路宽度与移动等级一致。
clearance-dynamic-gap|动态窄缝尺寸判定|建筑形成 24px 缝隙，验证小单位可过而大单位被可靠拒绝。
building-footprint-sizes|四档建筑占地|展示 Small、Medium、Large、Huge 四种业务 footprint 的尺寸和占用结果。
building-placement-rules|建筑放置规则|依次尝试重叠、越界、占用单位和制造假窄缝，确认返回稳定拒绝码。
building-connectivity-guard|全局连通性保护|尝试封死唯一通道，确认业务放置会因切断全局导航而被拒绝。
building-size-navigation|混合尺寸建筑群绕行|让编队穿过不同 footprint 的建筑场，验证占用与路径复验一致。
gameplay-profile-resource-runtime|Gameplay Profile 资源运行时|用 Godot Resource 配置三类单位和四类建筑，确认纯 C# 运行数据与资源一致。
clearance-editor-preview|编辑器净空预览|在编辑器覆盖层同时显示三档障碍膨胀、Portal 通行等级、连通分量和建筑占地。
clearance-bake-resource-runtime|Clearance Bake 资源运行时|加载版本化离线 Bake，确认静态连通位图、组件和 chunk 布局可直接用于预览。
attack-move-engage-resume|AttackMove 接敌后续行|单位沿攻击移动路线索敌、击杀，然后恢复原目标方向。
attack-move-leash-resume|AttackMove 超距脱战|敌人被引出追击 leash 后，确认单位放弃目标并回到原路线。
attack-move-command-isolation|Move 与 AttackMove 隔离|相邻两组分别执行普通 Move 和 AttackMove，确认只有后者主动接敌。
attack-move-cancel|Stop / Hold 取消攻击移动|交战过程中下达 Stop 与 Hold，确认追击被取消且两者保留不同索敌语义。
combat-melee-slots|近战唯一接触槽|多名近战围攻同一目标，验证每人预留独立接触位置而非堆叠。
combat-ranged-ring|远程稳定攻击环|多名远程单位接敌，确认在射程内形成互不重复的稳定开火环。
combat-stop-hold-acquire|Stop / Hold 索敌差异|Stop 可追击附近目标，Hold 只攻击射程内目标且不离开原位。
combat-multi-retarget|连续击杀与重选敌|多名攻击者穿过敌方队列，验证死亡清理、重新索敌和路线恢复。
queued-waypoints|Shift 多路点队列|为每个单位追加三个路点，确认它们严格按顺序执行。
queued-command-replace|即时命令替换队列|已有 Shift 队列时下达普通命令，确认整条待执行队列被替换。
queued-capacity-limit|命令队列容量上限|持续追加超过 16 条命令，验证固定容量与溢出策略稳定可预测。
control-group-recall|控制编组与召回|验证 Ctrl 覆盖、Shift 添加、数字键召回以及失效单位过滤。
smart-command-sequence|SmartCommand 语义解析|连续点击友军位置、敌军和地面，确认分别解析为移动、锁定攻击与移动。
operation-selection-camera|选择与相机操作|验证同类型双击选择、边缘滚动、光标锚定缩放和编组双击定位。
minimap-interaction|Minimap 坐标与指令|检查小地图绘制、世界坐标往返、视口框、镜头定位和右键 SmartCommand。
command-log-replay|命令日志精确回放|记录业务命令后以固定 Tick 重放，确认规范字节和最终状态 Hash 完全一致。
command-replay-divergence|回放首次分歧定位|修改一条重放命令的目标，确认系统能定位首个状态 Hash 分歧 Tick。
replay-package-world|Replay Package 重建世界|从资源身份、初始单位建筑和动态世界命令重建场景，再验证最终状态一致。
replay-checkpoint-resume|Checkpoint 中途恢复|在中间 Tick 保存版本化检查点，恢复后继续运行并逐段对比状态 Hash。
replay-checkpoint-choke|狭口私有状态 Checkpoint|在双向交通进行中恢复，确认方向租约等私有状态也被完整保存。
replay-hot-snapshot|热快照直接恢复|深拷贝战斗、路径、队列、建筑和狭口运行态，不重演早期 Tick 即可继续一致运行。
`.trim().split("\n");

const caseInfo = Object.fromEntries(caseRows.map(row => {
  const [id, title, test] = row.split("|");
  return [id, { title, test }];
}));

function categoryFor(id) {
  if (id.startsWith("destination-") || id === "shared-target-reservations") return "destination";
  if (id.includes("choke") || id === "portal-choke" || id === "reverse-choke" || id.includes("recovery")) return "choke";
  if (id.startsWith("dynamic-") || id === "navigation-resource-runtime") return "dynamic";
  if (id.startsWith("clearance-") || id.startsWith("building-") || id === "gameplay-profile-resource-runtime") return "clearance";
  if (id.startsWith("attack-") || id.startsWith("combat-")) return "combat";
  if (id.startsWith("queued-") || id === "control-group-recall" || id === "smart-command-sequence") return "operation";
  if (id.startsWith("replay-") || id.startsWith("command-log") || id.startsWith("command-replay")) return "replay";
  if (id === "operation-selection-camera" || id === "minimap-interaction") return "interface";
  return "movement";
}

const state = { recordings: [], selected: null, category: "all", query: "", history: false };
const $ = selector => document.querySelector(selector);
const els = {
  caseList: $("#case-list"), filters: $("#filters"), search: $("#search-input"),
  historyToggle: $("#history-toggle"), viewer: $("#viewer"), content: $("#viewer-content"),
  loading: $("#loading-state"), video: $("#detail-video"), toast: $("#toast")
};

const formatBytes = bytes => bytes < 1024 * 1024
  ? `${Math.round(bytes / 1024)} KB`
  : `${(bytes / 1024 / 1024).toFixed(1)} MB`;

function formatDate(value, withTime = true) {
  const date = new Date(value);
  return new Intl.DateTimeFormat("zh-CN", {
    year: "numeric", month: "2-digit", day: "2-digit",
    ...(withTime ? { hour: "2-digit", minute: "2-digit", hour12: false } : {})
  }).format(date);
}

function latestRecordings() {
  const seen = new Set();
  return state.recordings.filter(item => !seen.has(item.id) && seen.add(item.id));
}

function filteredRecordings() {
  const base = state.history ? state.recordings : latestRecordings();
  const query = state.query.trim().toLocaleLowerCase("zh-CN");
  return base.filter(item => {
    const meta = caseInfo[item.id] || {};
    const category = categories[categoryFor(item.id)];
    const matchesCategory = state.category === "all" || categoryFor(item.id) === state.category;
    const haystack = [item.id, item.display_name, meta.title, meta.test, category.label, category.feature].join(" ").toLocaleLowerCase("zh-CN");
    return matchesCategory && (!query || haystack.includes(query));
  });
}

function renderFilters() {
  const counts = Object.fromEntries(Object.keys(categories).map(key => [key, 0]));
  latestRecordings().forEach(item => counts[categoryFor(item.id)]++);
  const filters = [{ key: "all", label: "全部", count: latestRecordings().length }, ...Object.entries(categories).map(([key, value]) => ({ key, label: value.label, count: counts[key] }))];
  els.filters.innerHTML = filters.map(filter => `<button class="filter-button" type="button" data-filter="${filter.key}" aria-pressed="${state.category === filter.key}">${filter.label} · ${filter.count}</button>`).join("");
  els.filters.querySelectorAll("button").forEach(button => button.addEventListener("click", () => {
    state.category = button.dataset.filter;
    renderFilters();
    renderList();
  }));
}

function renderList() {
  const items = filteredRecordings();
  $("#visible-count").textContent = state.history ? `${items.length} 段录制` : `${items.length} 个场景`;
  if (!items.length) {
    els.caseList.innerHTML = `<div class="empty-list">没有找到匹配的测试<br>换个关键词试试看</div>`;
    return;
  }
  els.caseList.innerHTML = items.map((item, index) => {
    const meta = caseInfo[item.id] || { title: item.display_name || item.id };
    const secondary = state.history ? `${categories[categoryFor(item.id)].label} · ${formatDate(item.created_at)}` : categories[categoryFor(item.id)].label;
    return `<button class="case-item" type="button" data-key="${item.batch}/${item.id}" aria-current="${state.selected === item}">
      <span class="case-index">${String(index + 1).padStart(2, "0")}</span>
      <span class="case-copy"><strong>${meta.title}</strong><small>${secondary}</small></span>
      <span class="case-state ${item.status}" title="${item.status}"></span>
    </button>`;
  }).join("");
  els.caseList.querySelectorAll("button").forEach(button => button.addEventListener("click", () => {
    const [batch, id] = button.dataset.key.split("/");
    selectRecording(state.recordings.find(item => item.batch === batch && item.id === id), true, true);
  }));
}

function parseMetrics(text) {
  if (!text) return [];
  return text.split(/,\s*/).map(part => {
    const index = part.indexOf("=");
    return index > 0 ? [part.slice(0, index), part.slice(index + 1)] : ["result", part];
  }).slice(0, 12);
}

function selectRecording(item, updateHash = true, autoplay = false) {
  if (!item) return;
  state.selected = item;
  const meta = caseInfo[item.id] || {
    title: item.display_name || item.id.replaceAll("-", " "),
    test: item.display_name || "该场景验证对应业务功能在固定 Tick 模拟中的实际运行结果。"
  };
  const category = categories[categoryFor(item.id)];
  $("#detail-category").textContent = category.label;
  $("#detail-title").textContent = meta.title;
  $("#detail-subtitle").textContent = item.display_name || item.id;
  $("#detail-test").textContent = meta.test;
  $("#detail-feature").textContent = category.feature;
  $("#detail-date").textContent = `录制于 ${formatDate(item.created_at)}`;
  $("#detail-format").textContent = `${item.codec || "WebM"} · ${item.fps || 30} FPS`;
  $("#detail-size").textContent = formatBytes(item.bytes);
  $("#detail-status").textContent = item.status === "passed" ? "测试通过" : item.status === "failed" ? "测试失败" : "结果未知";
  $("#detail-status").className = `result-badge ${item.status}`;
  $("#log-link").href = item.log || "#";
  $("#log-link").hidden = !item.log;
  $("#raw-result").textContent = item.result || "这段历史记录没有保存结构化结果行。";
  $("#metric-list").innerHTML = parseMetrics(item.metrics).map(([key, value]) => `<dl class="metric"><dt>${key}</dt><dd>${value}</dd></dl>`).join("");
  els.video.pause();
  els.video.muted = true;
  els.video.autoplay = autoplay;
  els.video.src = item.video;
  els.video.load();
  if (autoplay) els.video.play().catch(() => {});

  const versions = state.recordings.filter(record => record.id === item.id);
  $("#history-section").hidden = versions.length < 2;
  $("#history-list").innerHTML = versions.map(record => `<div class="history-item">
    <span>${formatDate(record.created_at)}</span><span>${formatBytes(record.bytes)} · ${record.fps || 30} FPS</span>
    <button type="button" data-key="${record.batch}/${record.id}">${record === item ? "正在查看" : "查看此版"}</button>
  </div>`).join("");
  $("#history-list").querySelectorAll("button").forEach(button => button.addEventListener("click", () => {
    const [batch, id] = button.dataset.key.split("/");
    selectRecording(state.recordings.find(record => record.batch === batch && record.id === id), true, true);
  }));

  els.loading.hidden = true;
  els.content.hidden = false;
  renderList();
  if (updateHash) window.history.replaceState(null, "", `#${item.id}@${item.batch}`);
}

function selectFromHash() {
  const raw = decodeURIComponent(location.hash.slice(1));
  if (!raw) return null;
  const [id, batch] = raw.split("@");
  return state.recordings.find(item => item.id === id && (!batch || item.batch === batch));
}

function showToast(message) {
  els.toast.textContent = message;
  els.toast.classList.add("show");
  clearTimeout(showToast.timer);
  showToast.timer = setTimeout(() => els.toast.classList.remove("show"), 1800);
}

async function init() {
  try {
    const response = await fetch("videos.json", { cache: "no-store" });
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    const data = await response.json();
    state.recordings = data.recordings;
    const latest = latestRecordings();
    $("#case-count").textContent = data.case_count;
    $("#recording-count").textContent = data.recording_count;
    $("#pass-count").textContent = latest.filter(item => item.status === "passed").length;
    renderFilters();
    selectRecording(selectFromHash() || latest[0], false);
  } catch (error) {
    els.loading.innerHTML = `<p><strong>没有读到视频索引</strong><br><br>请先运行 <code>.\\tools\\generate_test_video_showcase.ps1</code><br>并通过本地 HTTP 服务打开此页面。</p>`;
    console.error(error);
  }
}

els.search.addEventListener("input", event => { state.query = event.target.value; renderList(); });
els.historyToggle.addEventListener("change", event => { state.history = event.target.checked; renderList(); });
document.addEventListener("keydown", event => {
  if (event.key === "/" && document.activeElement !== els.search) { event.preventDefault(); els.search.focus(); }
  if (event.key === "Escape" && document.activeElement === els.search) { els.search.value = ""; state.query = ""; els.search.blur(); renderList(); }
});
$("#copy-link").addEventListener("click", async () => {
  try { await navigator.clipboard.writeText(location.href); showToast("链接已复制"); }
  catch { showToast("请从地址栏复制链接"); }
});
window.addEventListener("hashchange", () => { const item = selectFromHash(); if (item && item !== state.selected) selectRecording(item, false); });

init();
