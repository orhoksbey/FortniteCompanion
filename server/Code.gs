/**
 * Fortnite Companion — Ekip sunucusu (Google Apps Script + Google Sheets)
 *
 * Kurulum: server/KURULUM.md
 *  1) Yeni bir Google E-Tablosu → Uzantılar → Apps Script → bu dosyayı ve Admin.html'i yapıştırın.
 *  2) "setup" fonksiyonunu bir kez çalıştırın: sayfaları oluşturur, EKİP ve YÖNETİCİ anahtarlarını üretir.
 *  3) Dağıt → Yeni dağıtım → Web uygulaması → "Beni" olarak yürüt, Erişim: "Herkes".
 *
 * Uç noktalar
 *  POST {action:"push", key, payload}        → oyuncunun kendi toplamlarını yazar, sıralamayı döndürür
 *  GET  ?action=leaderboard&key=EKİP_ANAHTARI → sıralama (JSON)
 *  GET  ?admin=YÖNETİCİ_ANAHTARI              → yönetici paneli (HTML)
 *  google.script.run.adminAdjust(...)         → yönetici bir oyuncuya sandık ekler/çıkarır (panelden)
 *
 * Yönetici düzeltmesi: "Duzeltme" sütununda oyuncu başına birikimli tutulur, her işlem "Duzeltmeler" sayfasına
 * yazılır. Oyuncunun uygulaması kendi toplamını (düzeltmesiz) gönderir; sunucu düzeltmeyi üstüne ekler ve
 * push yanıtında oyuncuya geri bildirir, böylece oyuncunun ekranındaki sayı da aynı olur.
 *
 * Güvenlik: EKİP anahtarı uygulamaya gömülüdür ve rastgele istekleri engeller. Her cihaz kendi gizli anahtarını
 * üretir; bir oyuncunun satırını yalnızca o cihaz güncelleyebilir (sunucuda yalnızca SHA-256 özeti saklanır).
 * Yönetici paneli ayrı bir anahtarla korunur. Yönetici, Oyuncular sayfasında "Gizli" sütununa TRUE yazarak
 * birini sıralamadan çıkarabilir.
 */

const PLAYER_HEADERS = [
  'DeviceId', 'SecretHash', 'DisplayName', 'EpicNames', 'TotalChests', 'TotalMatches', 'TotalKills',
  'ChestsPerMatch', 'KillsPerMatch', 'Today', 'Week', 'Season', 'BestMatch', 'BaselineChests',
  'BaselineMatches', 'BaselineKills', 'TrackedMatches', 'TrackedChests', 'LastMatchUtc', 'UpdatedUtc',
  'AppVersion', 'Gizli', 'FirstSeenUtc', 'Duzeltme'
];
const ADJUST_HEADERS = ['TimeUtc', 'DeviceId', 'DisplayName', 'Delta', 'NewTotalAdjust', 'Note'];
const MATCH_HEADERS = [
  'DeviceId', 'DisplayName', 'SessionId', 'StartedUtc', 'Chests', 'Kills', 'Source', 'Playlist',
  'Placement', 'DurationSeconds', 'ReceivedUtc'
];
const MAX_MATCHES_PER_PUSH = 300;

// Bu sütunlar düz metin olarak biçimlenir: Sheets, ISO tarihleri Date'e, "12e34…" gibi onaltılık kimlikleri
// sayıya çevirmesin.
const TEXT_COLUMNS = {
  Oyuncular: ['DeviceId', 'SecretHash', 'DisplayName', 'EpicNames', 'LastMatchUtc', 'UpdatedUtc', 'AppVersion', 'FirstSeenUtc'],
  Maclar: ['DeviceId', 'DisplayName', 'SessionId', 'StartedUtc', 'Source', 'Playlist', 'ReceivedUtc'],
  Duzeltmeler: ['TimeUtc', 'DeviceId', 'DisplayName', 'Note']
};

// ---------------------------------------------------------------- kurulum

function setup() {
  const props = PropertiesService.getScriptProperties();
  if (!props.getProperty('TEAM_KEY')) props.setProperty('TEAM_KEY', 'fc-' + Utilities.getUuid().replace(/-/g, ''));
  if (!props.getProperty('ADMIN_KEY')) props.setProperty('ADMIN_KEY', 'adm-' + Utilities.getUuid().replace(/-/g, ''));
  sheet_('Oyuncular', PLAYER_HEADERS);
  sheet_('Maclar', MATCH_HEADERS);
  sheet_('Duzeltmeler', ADJUST_HEADERS);
  Logger.log('EKİP ANAHTARI (uygulamaya): ' + props.getProperty('TEAM_KEY'));
  Logger.log('YÖNETİCİ ANAHTARI (yalnızca sizde kalsın): ' + props.getProperty('ADMIN_KEY'));
}

function sheet_(name, headers) {
  const ss = SpreadsheetApp.getActiveSpreadsheet();
  let sh = ss.getSheetByName(name);
  if (!sh) {
    sh = ss.insertSheet(name);
  }
  if (sh.getLastRow() === 0) {
    sh.getRange(1, 1, 1, headers.length).setValues([headers]).setFontWeight('bold');
    sh.setFrozenRows(1);
    (TEXT_COLUMNS[name] || []).forEach(function (h) {
      const c = headers.indexOf(h) + 1;
      if (c > 0) sh.getRange(1, c, sh.getMaxRows(), 1).setNumberFormat('@');
    });
  } else {
    // Eski sürümden kalan tablo: eksik sütunları sona ekle (veriler korunur)
    const have = sh.getRange(1, 1, 1, sh.getLastColumn()).getValues()[0];
    const missing = headers.filter(function (h) { return have.indexOf(h) < 0; });
    if (missing.length > 0) {
      sh.getRange(1, have.length + 1, 1, missing.length).setValues([missing]).setFontWeight('bold');
    }
  }
  return sh;
}

// ---------------------------------------------------------------- HTTP

function doGet(e) {
  const p = (e && e.parameter) || {};
  const props = PropertiesService.getScriptProperties();
  if (p.admin && p.admin === props.getProperty('ADMIN_KEY')) {
    const t = HtmlService.createTemplateFromFile('Admin');
    t.dataJson = JSON.stringify(adminData_());
    t.adminKeyJson = JSON.stringify(String(p.admin));
    return t.evaluate()
      .setTitle('Fortnite Companion — Yönetici')
      .addMetaTag('viewport', 'width=device-width, initial-scale=1');
  }
  if (p.action === 'leaderboard') {
    if (p.key !== props.getProperty('TEAM_KEY')) return json_({ ok: false, error: 'yetkisiz' });
    return json_({ ok: true, serverTimeUtc: new Date().toISOString(), players: leaderboard_() });
  }
  return HtmlService.createHtmlOutput('<p style="font-family:sans-serif">Fortnite Companion ekip sunucusu çalışıyor.</p>');
}

function doPost(e) {
  let body;
  try {
    body = JSON.parse((e && e.postData && e.postData.contents) || '{}');
  } catch (err) {
    return json_({ ok: false, error: 'geçersiz JSON' });
  }
  const props = PropertiesService.getScriptProperties();
  if (body.key !== props.getProperty('TEAM_KEY')) return json_({ ok: false, error: 'yetkisiz' });

  if (body.action === 'leaderboard') {
    return json_({ ok: true, serverTimeUtc: new Date().toISOString(), players: leaderboard_() });
  }
  if (body.action !== 'push' || !body.payload) return json_({ ok: false, error: 'bilinmeyen işlem' });

  const lock = LockService.getScriptLock();
  if (!lock.tryLock(20000)) return json_({ ok: false, error: 'sunucu meşgul, daha sonra tekrar denenecek' });
  let result;
  try {
    result = upsertPlayer_(body.payload);
    if (!result.ok) return json_(result);
    upsertMatches_(body.payload);
  } finally {
    lock.releaseLock();
  }
  return json_({
    ok: true,
    serverTimeUtc: new Date().toISOString(),
    self: { adjustChests: result.adjust || 0 },
    players: leaderboard_()
  });
}

function json_(obj) {
  return ContentService.createTextOutput(JSON.stringify(obj)).setMimeType(ContentService.MimeType.JSON);
}

// ---------------------------------------------------------------- veri

function sha256_(text) {
  const bytes = Utilities.computeDigest(Utilities.DigestAlgorithm.SHA_256, String(text), Utilities.Charset.UTF_8);
  return bytes.map(function (b) { return ('0' + (b & 0xff).toString(16)).slice(-2); }).join('');
}

function str_(v, max) { return String(v == null ? '' : v).slice(0, max || 100); }
function iso_(v) { return v instanceof Date ? v.toISOString() : (v ? String(v) : null); }
function sint_(v) { const n = Math.trunc(Number(v)); return isFinite(n) && Math.abs(n) < 1000000 ? n : 0; }
function int_(v) { const n = Math.floor(Number(v)); return isFinite(n) && n >= 0 && n < 10000000 ? n : 0; }
function num_(v) { const n = Number(v); return isFinite(n) && n >= 0 && n < 100000 ? Math.round(n * 100) / 100 : 0; }

function upsertPlayer_(p) {
  const deviceId = str_(p.deviceId, 64);
  if (!/^[a-f0-9]{32}$/i.test(deviceId) || !p.deviceSecret) return { ok: false, error: 'geçersiz cihaz' };
  const name = str_(p.displayName, 40).trim();
  if (!name) return { ok: false, error: 'ad boş' };

  const sh = sheet_('Oyuncular', PLAYER_HEADERS);
  const data = sh.getDataRange().getValues();
  const col = indexOf_(data[0]);
  const hash = sha256_(p.deviceSecret);
  const now = new Date().toISOString();

  let rowIndex = -1;
  for (let i = 1; i < data.length; i++) {
    if (data[i][col.DeviceId] === deviceId) { rowIndex = i; break; }
  }
  if (rowIndex > 0 && data[rowIndex][col.SecretHash] !== hash) {
    return { ok: false, error: 'bu cihaz kimliği başka bir cihaza ait' };
  }

  const row = rowIndex > 0 ? data[rowIndex].slice() : data[0].map(function () { return ''; });
  const set = function (k, v) { row[col[k]] = v; };
  set('DeviceId', deviceId);
  set('SecretHash', hash);
  set('DisplayName', name);
  set('EpicNames', str_(p.epicNames, 120));
  set('TotalChests', int_(p.totalChests));
  set('TotalMatches', int_(p.totalMatches));
  set('TotalKills', int_(p.totalKills));
  set('ChestsPerMatch', num_(p.chestsPerMatch));
  set('KillsPerMatch', num_(p.killsPerMatch));
  set('Today', int_(p.today));
  set('Week', int_(p.week));
  set('Season', int_(p.season));
  set('BestMatch', int_(p.bestMatch));
  set('BaselineChests', int_(p.baselineChests));
  set('BaselineMatches', int_(p.baselineMatches));
  set('BaselineKills', int_(p.baselineKills));
  set('TrackedMatches', int_(p.trackedMatches));
  set('TrackedChests', int_(p.trackedChests));
  set('LastMatchUtc', str_(p.lastMatchUtc, 40));
  set('UpdatedUtc', now);
  set('AppVersion', str_(p.appVersion, 20));
  if (rowIndex < 0) {
    set('Gizli', false);
    set('FirstSeenUtc', now);
    set('Duzeltme', 0);
    sh.appendRow(row);
  } else {
    sh.getRange(rowIndex + 1, 1, 1, row.length).setValues([row]);
  }
  return { ok: true, adjust: sint_(row[col.Duzeltme]) };
}

function upsertMatches_(p) {
  const list = Array.isArray(p.matches) ? p.matches.slice(0, MAX_MATCHES_PER_PUSH) : [];
  if (list.length === 0) return;
  const deviceId = str_(p.deviceId, 64);
  const sh = sheet_('Maclar', MATCH_HEADERS);
  const data = sh.getDataRange().getValues();
  const col = indexOf_(data[0]);
  const existing = {};
  for (let i = 1; i < data.length; i++) {
    if (data[i][col.DeviceId] === deviceId) existing[data[i][col.SessionId]] = i;
  }
  const now = new Date().toISOString();
  const append = [];
  list.forEach(function (m) {
    const session = str_(m.sessionId, 80);
    if (!session) return;
    const row = [deviceId, str_(p.displayName, 40), session, str_(m.startedUtc, 40), int_(m.chests),
      m.kills == null ? '' : int_(m.kills), str_(m.source, 20), str_(m.playlist, 60),
      m.placement == null ? '' : int_(m.placement), m.durationSeconds == null ? '' : int_(m.durationSeconds), now];
    if (existing[session] != null) {
      const r = existing[session];
      // Yalnızca değişen satırı yaz (kotayı korur)
      if (String(data[r][col.Chests]) !== String(row[4]) || String(data[r][col.Kills]) !== String(row[5])) {
        sh.getRange(r + 1, 1, 1, row.length).setValues([row]);
      }
    } else {
      append.push(row);
    }
  });
  if (append.length > 0) {
    sh.getRange(sh.getLastRow() + 1, 1, append.length, MATCH_HEADERS.length).setValues(append);
  }
}

function indexOf_(headerRow) {
  const map = {};
  headerRow.forEach(function (h, i) { map[h] = i; });
  return map;
}

function players_() {
  const sh = sheet_('Oyuncular', PLAYER_HEADERS);
  const data = sh.getDataRange().getValues();
  if (data.length < 2) return [];
  const col = indexOf_(data[0]);
  const out = [];
  for (let i = 1; i < data.length; i++) {
    const r = data[i];
    if (!r[col.DeviceId]) continue;
    const appChests = Number(r[col.TotalChests]) || 0;
    const adjust = col.Duzeltme == null ? 0 : sint_(r[col.Duzeltme]);
    const matches = Number(r[col.TotalMatches]) || 0;
    out.push({
      deviceId: String(r[col.DeviceId]),
      displayName: r[col.DisplayName],
      epicNames: r[col.EpicNames],
      totalChests: Math.max(0, appChests + adjust),
      appChests: appChests,
      adjust: adjust,
      totalMatches: matches,
      totalKills: Number(r[col.TotalKills]) || 0,
      chestsPerMatch: matches > 0 ? Math.round(Math.max(0, appChests + adjust) / matches * 100) / 100 : 0,
      killsPerMatch: Number(r[col.KillsPerMatch]) || 0,
      today: Number(r[col.Today]) || 0,
      week: Number(r[col.Week]) || 0,
      season: Number(r[col.Season]) || 0,
      bestMatch: Number(r[col.BestMatch]) || 0,
      trackedMatches: Number(r[col.TrackedMatches]) || 0,
      lastMatchUtc: iso_(r[col.LastMatchUtc]),
      updatedUtc: iso_(r[col.UpdatedUtc]),
      firstSeenUtc: iso_(r[col.FirstSeenUtc]),
      appVersion: r[col.AppVersion],
      hidden: r[col.Gizli] === true || String(r[col.Gizli]).toUpperCase() === 'TRUE'
    });
  }
  return out;
}

/** Uygulamalara giden sıralama: gizli anahtar özeti ve gizlenen oyuncular hariç. */
function leaderboard_() {
  return players_()
    .filter(function (p) { return !p.hidden; })
    .sort(function (a, b) { return b.totalChests - a.totalChests; });
}

/** Yönetici paneli verisi: tüm oyuncular + her oyuncunun son 30 maçı. */
function adminData_() {
  const players = players_().sort(function (a, b) { return b.totalChests - a.totalChests; });
  const sh = sheet_('Maclar', MATCH_HEADERS);
  const data = sh.getDataRange().getValues();
  const col = indexOf_(data[0]);
  const byDevice = {};
  for (let i = 1; i < data.length; i++) {
    const r = data[i];
    const d = r[col.DeviceId];
    if (!d) continue;
    (byDevice[d] = byDevice[d] || []).push({
      startedUtc: iso_(r[col.StartedUtc]) || '',
      chests: Number(r[col.Chests]) || 0,
      kills: r[col.Kills] === '' ? null : Number(r[col.Kills]),
      source: String(r[col.Source] || ''),
      playlist: String(r[col.Playlist] || ''),
      placement: r[col.Placement] === '' ? null : Number(r[col.Placement])
    });
  }
  players.forEach(function (p) {
    p.matches = (byDevice[p.deviceId] || [])
      .sort(function (a, b) { return b.startedUtc < a.startedUtc ? -1 : 1; })
      .slice(0, 30);
  });
  const log = sheet_('Duzeltmeler', ADJUST_HEADERS).getDataRange().getValues();
  const lcol = indexOf_(log[0]);
  const adjustments = [];
  for (let i = log.length - 1; i >= 1 && adjustments.length < 30; i--) {
    adjustments.push({
      timeUtc: iso_(log[i][lcol.TimeUtc]), deviceId: String(log[i][lcol.DeviceId]),
      displayName: String(log[i][lcol.DisplayName]), delta: sint_(log[i][lcol.Delta]), note: String(log[i][lcol.Note] || '')
    });
  }
  return {
    generatedUtc: new Date().toISOString(), players: players, adjustments: adjustments,
    sheetUrl: SpreadsheetApp.getActiveSpreadsheet().getUrl()
  };
}

// ---------------------------------------------------------------- yönetici işlemleri (panelden google.script.run)

function requireAdmin_(adminKey) {
  const k = PropertiesService.getScriptProperties().getProperty('ADMIN_KEY');
  if (!k || String(adminKey) !== k) throw new Error('Yönetici anahtarı geçersiz.');
}

/** Paneldeki "Yenile" düğmesi. */
function adminRefresh(adminKey) {
  requireAdmin_(adminKey);
  return JSON.stringify(adminData_());
}

/**
 * Bir oyuncuya sandık ekler (pozitif) veya çıkarır (negatif). Birikimli "Duzeltme" sütununa eklenir ve
 * "Duzeltmeler" sayfasına kaydedilir. Oyuncunun uygulaması bir sonraki eşitlemede (en geç ~10 dk) bunu alır.
 */
function adminAdjust(adminKey, deviceId, delta, note) {
  requireAdmin_(adminKey);
  const d = sint_(delta);
  if (d === 0 || Math.abs(d) > 100000) throw new Error('Geçerli bir sayı girin (ör. 7 veya -3).');
  const lock = LockService.getScriptLock();
  if (!lock.tryLock(20000)) throw new Error('Sunucu meşgul, tekrar deneyin.');
  try {
    const sh = sheet_('Oyuncular', PLAYER_HEADERS);
    const data = sh.getDataRange().getValues();
    const col = indexOf_(data[0]);
    let rowIndex = -1;
    for (let i = 1; i < data.length; i++) {
      if (String(data[i][col.DeviceId]) === String(deviceId)) { rowIndex = i; break; }
    }
    if (rowIndex < 0) throw new Error('Oyuncu bulunamadı.');
    const current = sint_(data[rowIndex][col.Duzeltme]);
    const appChests = Number(data[rowIndex][col.TotalChests]) || 0;
    const next = current + d;
    if (appChests + next < 0) throw new Error('Toplam sandık sıfırın altına düşemez.');
    sh.getRange(rowIndex + 1, col.Duzeltme + 1).setValue(next);
    sheet_('Duzeltmeler', ADJUST_HEADERS).appendRow([
      new Date().toISOString(), String(deviceId), String(data[rowIndex][col.DisplayName]), d, next, str_(note, 200)
    ]);
  } finally {
    lock.releaseLock();
  }
  return JSON.stringify(adminData_());
}
