const fs=require('node:fs');
const path=require('node:path');
const {chromium}=require('C:/Program Files/WindowsApps/OpenAI.CodexPrimaryRuntime.v26-1026-487-0_26.1026.487.0_x64__3k8sg7r9htsxt/dependencies/node/node_modules/playwright');
const root='C:/Projects/BackHome';
const out=root+'/output/pdf/BackHome-performance-report-2026-11-03.pdf';
const raw=JSON.parse(fs.readFileSync(root+'/Logs/performance-audit-2026-11-03/browser/summary.json'));
const editor=JSON.parse(fs.readFileSync(root+'/Logs/performance-audit-2026-11-03/editor-summary.json'));
const names={idle:'עמידה בעולם',movement:'תנועה במגע',after_movement:'לאחר תנועה',ship_transition:'מעבר לחללית',ship_idle:'עמידה בחללית',galaxy_transition:'חזרה לעולם',galaxy_return_idle:'לאחר החזרה'};
const f=(x,n=1)=>Number(x).toFixed(n);
const table=(heads,rows)=>`<table><thead><tr>${heads.map(x=>`<th>${x}</th>`).join('')}</tr></thead><tbody>${rows.map(row=>`<tr>${row.map(x=>`<td>${x}</td>`).join('')}</tr>`).join('')}</tbody></table>`;
const section=(title,body,sub='')=>`<section><header>BACKHOME <span>ביקורת ביצועים · 03.11.2026</span></header><h1>${title}</h1>${sub?`<p class="muted">${sub}</p>`:''}${body}</section>`;
const pages=[];
pages.push(section('דוח ביצועים למובייל',`
<p class="subtitle">הגרסה המיועדת לבדיקות ב־itch.io</p>
<div class="verdict"><h2>יש בסיס טוב, אבל עדיין אין אישור<br>ל־30 FPS יציבים בטלפונים</h2><p>על המחשב החזק המשחק פועל סביב מגבלת 30 FPS. האטת המעבד חשפה ירידות בתנועה ועצירות במעבר בין סצנות. נדרש סבב שיפורים ואימות על מכשירים אמיתיים.</p></div>
<div class="metric"><b>30.06 FPS</b><span>ממוצע בזמן תנועה, ללא האטת CPU</span></div>
<div class="metric"><b>27.98 FPS</b><span>בתנועה בהאטה פי 6; מינימום של 24 בחלון שנייה</span></div>
<div class="metric"><b>993 ms</b><span>הפער הארוך ביותר בחזרה לעולם, בהאטה פי 6</span></div>
<h2>המסקנה המעשית</h2><p>לתקן קודם את אנימציית החללית, להוזיל קווי מתאר ולקבוע מצב איכות נמוך. אחר כך לטפל בטעינות, בגופנים, בחלקיקים ובאפקטים.</p>
<p class="note">האטת CPU על מחשב אינה בדיקה של טלפון. הדוח אינו אישור לתמיכה בכל מכשיר. לא בוצע ביילד נוסף לאחר ההנחיה שלך; המדידות בדפדפן השתמשו בקבצים שכבר נוצרו.</p>`));

for(const run of [raw[0],raw[2]]){
 pages.push(section(run.rate===1?'מדידות דפדפן: ללא האטה':'מדידות דפדפן: האטה פי 6',
 `<p>הטבלה מציגה קצב ציור ממוצע, את החלון המלא החלש ביותר של שנייה, ואת הפער הארוך ביותר בין פריימים.</p>`+
 table(['תרחיש','FPS ממוצע','מינימום בשנייה','פער מרבי (ms)'],run.scenarios.map(s=>[names[s.name],f(s.fps,2),s.minimumFullSecondFPS,f(s.maxMs)]))+
 `<h2>${run.rate===1?'רוב המשחק סביב 30, אך יש עצירות':'היעד של 30 אינו נשמר בתנאי הבדיקה'}</h2>`+
 (run.rate===1?`<p>ב־30 שניות תנועה נרשם פער אחד מעל 50 ms. החזרה לעולם הוסיפה פער של 145.7 ms. בזמן שהייה בחללית הופיעו 679 הודעות שגיאה מאותו סוג.</p><p>זמן מוכנות משרת מקומי: 1.054 שניות. בדיקת ההמשך לאחר תנועה נמשכה כ־60 שניות; יתר תרחישי העמידה נמשכו 15–20 שניות, והמעברים כ־7 שניות.</p>`:
 `<p>בכ־30 שניות תנועה נרשמו 10 פערים מעל 50 ms. ה־p95 היה 41.7 ms וה־p99 היה 55.6 ms. החזרה לעולם כללה עצירה של כמעט שנייה; זו אינה ירידה מתמשכת ל־1 FPS במהלך משחק רגיל.</p><p>בבדיקה הנוספת עם האטה פי 4 נמדדו בתנועה 28.73 FPS בממוצע, מינימום של 28 בחלון שנייה ופער מרבי של 69.5 ms. החללית לא נבדקה בריצה זו.</p>`)+
 `<p class="note">מקור: browser/summary.json, ‏03.11.2026. המדידה מבוססת על מרווחים בין הגשות ציור WebGL. GPU נשאר RTX 3070. תזמון סביבת הבדיקה היה בקפיצות של כ־6.94 ms ולכן גם מנגנון הגבלת הפריימים עשוי להשפיע על המספרים. לא נמדד זמן GPU.</p>`));
}

pages.push(section('עומס רינדור: קווי המתאר',
`<p>שישה תרחישים ב־Unity Editor, כ־25 שניות לכל תרחיש אחרי חימום. נוספו אויבים עמידים כדי לשמור על ירי רצוף. מספר היצורים כולל גם את האויבים הטבעיים.</p>`+
table(['תרחיש','פעילים','אלפי משולשים / פריים','קריאות ציור / פריים'],editor.map((s,i)=>[['עמידה','הליכה','12 אויבים נוספים','36 אויבים נוספים','מינימאפ כבוי','קווי מתאר כבויים'][i],s.minCreatures===s.maxCreatures?s.maxCreatures:`${s.minCreatures}–${s.maxCreatures}`,f(s.meanTriangles/1000),f(s.meanDrawCalls)]))+
`<div class="verdict"><h2>כ־67% פחות משולשים<br>וכ־37% פחות קריאות ציור</h2><p>זה ההפרש בעומס של 45 יצורים כשמכבים את תכונת קווי המתאר. זו נקודת שיפור חזקה לפרופיל טלפון חלש.</p></div>
<p><b>אין לפרש זאת כשיפור של 67% ב־FPS.</b> מדידת PlayerLoop לא השתפרה בניסוי הזה. יש לבדוק על GPU של טלפון את העלות ואת התועלת בפועל.</p>
<p>כיבוי המינימאפ בעומס סטטי לא הראה שיפור ברור. בהליכה נמדדו בו כ־0.070 ms בממוצע ב־Editor, ולכן הוא אינו היעד הראשון לשינוי.</p>
<p class="note">מקור: editor-summary.json ונתוני CSV, ‏03.11.2026; סך הכול 21,322 פריימים. אלו ממוצעי UnityStats, לא זמני GPU. PlayerLoop אינו כולל כאן את כל עבודת הרינדור של ה־Editor, ואין להמיר את זמניו ל־FPS בטלפון.</p>`));

pages.push(section('תיקונים ראשונים לפני בטא רחבה',`
<h2>1. אנימציית הידיים בחללית</h2><p>נמצאו 679 הודעות שגיאה ללא האטה ו־640 בהאטה פי 6. הקוד משבית את ה־Animator וקורא SampleAnimation בכל LateUpdate. יש לעבור לניגון תקין בזמן ריצה באמצעות Animator או Playables, ולוודא שהידיים זזות נכון ושה־Console נקי.</p><p class="code">ShipPilotHands.cs:67, 106–108</p>
<h2>2. קווי מתאר ומצב איכות נמוך</h2><p>לבחון קווי מתאר רק לשחקן, לבוס ולאויבים קרובים; לצמצם מעברי רינדור ולבטל ארבע הקצאות ToArray בכל פריים. לבחון בנפרד MSAA של 2x או ללא MSAA מול 4x הנוכחי, מכסת חלקיקים ורזולוציה נמוכה יותר. לשמור על קריאות האויבים.</p><p class="code">ToonOutlineRendererFeature.cs:130, 235<br>PhonePerformance.cs:34</p>
<h2>3. מעברי סצנות</h2><p>הטעינה מתבצעת באופן סינכרוני. להציג מסך טעינה, לבחון טעינה אסינכרונית ולפצל או להכין מראש יצירת עולם, ערפל ואובייקטים. טעינה אסינכרונית לבדה אינה מבטיחה שהפעלת הסצנה לא תחסום.</p><p class="code">PlanetHologram.cs:68 · ShipTakeoffPrompt.cs:233</p>
<h2>4. אטלסי גופנים</h2><p>למפות את הגדלים שבשימוש, לאחד אטלסים ולבחון אטלס קטן יותר או SDF מתאים. יש אטלס יחיד של 16 MB וכמה נכסי גופן של 4 MB. לבדוק קריאות בכל מסך ולא למחוק נכסים לפי שם בלבד.</p>
<p class="note">אלו המלצות שטרם יושמו. יש למדוד מחדש אחרי כל שינוי; לא הוכח עדיין הרווח הנפרד ב־FPS של כל תיקון.</p>`));

pages.push(section('סבב שני: עומס מתמשך',`
<h2>רישום חלקיקים במקום סריקה בכל פריים</h2><p>PlanetParticleGravity מחפש את כל מערכות החלקיקים בכל פריים. בעומס נמדדו בו 958 bytes בממוצע לפריים, מתוך 1,343 bytes ב־PlayerLoop. עדיף רישום וביטול רישום בעת יצירה, השבתה וחזרה למאגר.</p><p>קבוצת _seen שומרת הפניות עד יציאה מהסצנה; צריך לנקות אותן. זו אינה הוכחה לדליפת זיכרון native או GPU.</p><p class="code">PlanetParticleGravity.cs:26, 46</p>
<h2>שימוש חוזר בקליעים ובאפקטים</h2><p>ירי, הבזקי לוע, פגיעות ואפקטי מוות משתמשים במסלולי יצירה והשמדה. להרחיב את דפוס המאגרים שכבר קיים בשלל ובמספרי נזק, להכין כמות מוגבלת מראש ולאפס את מצב האובייקט בחזרתו למאגר.</p><p class="code">RangedWeapon.cs:174 · ChainHitEffect.cs<br>AreaBlastEffect.cs · CreatureDeathVfx.cs</p>
<h2>שכנים קרובים ופסי חיים</h2><p>בעומס, SlimeBody צרך בממוצע 0.385 ms ו־HpBar צרך 0.313 ms לפריים ב־Editor. יש שש איטרציות של בדיקות זוגות: כדאי לבחון חלוקה מרחבית לחיפוש שכנים ולצמצם עדכון פסי חיים רחוקים או ללא שינוי.</p>
<h2>מה כבר עובד לטובת המשחק</h2><p>פרופיל טלפון, מגבלת 30 FPS ב־Web, ביטול צללים ו־HDR, ‏SRP Batcher, סטרימינג של העולם, מאגרי שלל ונזק, רזולוציית רינדור מוגבלת ו־Brotli. אין צורך לשכתב את כל המשחק.</p>
<p class="note">לא הייתי משנה ראשון את המינימאפ. גם אין לבטל Depth Texture בלי חלופה למים שתלויים בו. זמני ה־CPU כאן הם של מחשב חזק ב־Editor.</p>`));

pages.push(section('טעינה וזיכרון',
table(['קובץ','דחוס (MB)','לאחר Brotli (MB)'],[['data.br','22.67','37.43'],['wasm.br','7.43','31.93'],['framework.js.br','0.066','0.324'],['כל הפלט','30.22','—']])+
`<p class="caption">MB עשרוני. מקור: build-files.json ו־build-result.txt.</p>
<div class="verdict"><h2>כ־25.5 שניות לעלייה ב־10 Mbps</h2><p>נמדד עם השהיית רשת של 80 ms. בשרת מקומי ללא הגבלת רשת נמדדו 1.05 / 2.96 / 4.77 שניות בהאטות CPU של ×1 / ×4 / ×6.</p></div>
<h2>הורדה אינה צריכת RAM</h2><p>בבדיקת טעינה נוספת נצפה זיכרון WebAssembly בגודל 256 MiB, שנשאר כך בזמן שינויי גודל התצוגה. זה גודל הזיכרון הליניארי שהוקצה, ולא כמות המידע החי או כלל זיכרון התהליך.</p>
<p>ה־JavaScript heap היציב היה בערך 49–53 MB. אין לחבר אותו אוטומטית למדידות אחרות כדי לקבוע תקציב זיכרון: טקסטורות, GPU והדפדפן מוסיפים צריכה שלא נמדדה כאן. הגדרת מקסימום של 2,048 MiB אינה מבטיחה שטלפון יכול להקצות אותה.</p>
<h2>הנכסים הגדולים</h2><p>בדוח הבנייה: 69.0 MB טקסטורות, 11.5 MB אנימציות ו־5.0 MB מודלים, מתוך 89.8 MB נכסי משתמש לא דחוסים. אטלס LilitaOne Outline 210 יחיד תורם 16.0 MB. המספרים אינם RAM ואינם גודל הורדה.</p>
<p class="note">מקור: build-log.txt ו־network/summary.json, ‏03.11.2026. זמן עלייה ב־itch.io תלוי גם במטמון, ב־CDN וברשת השחקן. תיעוד זיכרון Unity: מקור 3 בעמוד האחרון.</p>`));

pages.push(section('itch.io והתאמה לטלפונים',`
<h2>לקבוע מכשירי מינימום נתמכים</h2><p>אי אפשר להבטיח תמיכה בכל טלפון. בפרויקט מופעל WebAssembly 2023; דרישות הדפדפן הרלוונטיות כוללות Safari 16.4, ‏Chrome/Edge 91 או Firefox 89 לפחות, בנוסף ליכולות גרפיות ולזיכרון. דפדפן נתמך אינו הבטחה ל־30 FPS על חומרה חלשה. [1]</p>
<h2>בדיקה מתוך עמוד המשחק</h2><p>חבילת HTML5 צריכה index.html בשורש וקישורים יחסיים. itch.io תומך בהגשת קובצי .br עם כותרת Content-Encoding מתאימה. צריך לבדוק הפעלה במובייל, מסך מלא, מגע, קול וסיבוב מתוך עמוד ניסוי אמיתי; השרת המקומי אינו מאמת את ה־iframe וה־CDN. [2]</p>
<p>בבדיקה המקומית ה־Canvas רונדר ב־390×693. בסיבוב לרוחב הוא נשאר באותה רזולוציה והוצג בגודל 219×390 פיקסלי CSS. זה שומר על רינדור קטן, אך דורש בדיקת שימושיות במכשיר.</p>
<h2>מטריצת מכשירים מוצעת</h2><p>Android חלש עם 3–4 GB RAM, מכשיר ביניים ומכשיר חזק; iPhone ישן שעדיין עומד בדרישות הדפדפן ו־iPhone חדש. לבחור מינימום נתמך לפי תוצאות מדודות, ולא לפי האטת CPU בלבד.</p>
<h2>האפליקציה העתידית</h2><p>נדרשת בדיקת Android ו־iOS נפרדת. Native עשוי לשנות את עלות המעבד, אך לא מבטל עלות רינדור, טקסטורות, טעינות והתחממות. מומלץ להתחיל מיעד 30 FPS במכשיר החלש הנתמך ולהפעיל 60 לפי מדידה. כיום הקוד מכוון ל־60 ב־native.</p>
<p class="note">לא נבנתה ולא נבדקה כאן גרסת native. ה־ZIP הקיים מ־31.10.2026 ישן מצילום המצב שנבדק; הוא לא הוחלף ולא הועלה דבר.</p>`));

pages.push(section('תכנית אימות וגבולות הבדיקה',`
<h2>לפני הכרזה על 30 FPS</h2><p>תקציב הפריים הוא כ־33.3 ms. צריך להשאיר מרווח להתחממות ולעומסי מערכת, ולבדוק ממוצע, חלונות של שנייה, p95/p99 וקפיצות ארוכות. p95 לבדו אינו מוכיח שאין נפילות.</p>
<ul><li>10–15 דקות משחק רצוף על כל מכשיר, ושלוש חזרות לפחות לכל תרחיש.</li><li>בוס, גל אויבים מרבי, כל הנשקים והאפקטים, שלל ו־HUD.</li><li>מעבר בין העולם לחללית ומוות וחזרה, עשר פעמים; לבדוק שהזיכרון מתייצב אחרי חימום.</li><li>רקע וחזרה, נעילה, סיבוב וחיסכון בסוללה; לבדוק קלט, קול ואובדן WebGL.</li><li>אפס שגיאות חוזרות וקריסות; מדידת CPU, ‏GPU וזיכרון על המכשיר.</li></ul>
<h2>איך נמדד הדוח</h2><p>Unity 6000.6.0f1, ‏Ryzen 7 7800X3D, ‏RTX 3070 וכ־32 GB RAM. בדפדפן: Edge עם GPU שולחני, viewport של 390×844 ו־DPR של 3, הקשר חדש לכל ריצה, קלט מגע ומעברים דרך מתודות המשחק.</p>
<p>ב־Editor נבדק פרופיל טלפון משוכפל, Game View של 1080×1920 וקצב לא מוגבל. ריצה מוקדמת שנקטעה עקב טעינת קוד מחדש נפסלה. כלי המדידה מוסיפים תקורה מסוימת.</p>
<p class="note">לא נבדקו טלפונים פיזיים, Safari/iOS, התחממות או רשת itch.io האמיתית. ביילד הבדיקה הושלם לפני ההנחיה לא לבנות שוב; הוא כולל runInBackground=true לצורכי מדידה, אך הבדיקות נערכו כשהמסמך גלוי. הגדרות המקור שוחזרו והקוד הזמני הוסר. תיקוני הביצועים טרם יושמו.</p>`));

pages.push(section('מקורות ונתונים לשחזור',`
<p>הדוח מתייחס לצילום מצב הבדיקה מ־03.11.2026. שינויים שנעשו במקביל לאחר יצירת הקבצים אינם נכללים במדידות Web האלה.</p>
<h2>קובצי ראיות בפרויקט</h2><p>התיקייה הראשית:</p><p class="path">C:/Projects/BackHome/Logs/<br>performance-audit-2026-11-03/</p>
<ul><li><span dir="ltr">browser/summary.json</span> - כל תרחישי הדפדפן, FPS ושגיאות.</li><li><span dir="ltr">editor-summary.json</span> - סיכום תרחישי Editor; בתיקיית editor נמצאים קובצי המקור.</li><li><span dir="ltr">network/summary.json</span> - טעינה ברשת מוגבלת, WebAssembly ורזולוציות.</li><li><span dir="ltr">build-log.txt</span> - פירוט גדלי הנכסים.</li><li><span dir="ltr">verification.json</span> - אימות שחזור ההגדרות והסרת קוד זמני.</li></ul>
<p>כלי הבדיקה החוזרת נמצא ב־<span dir="ltr">tools/validation/performance-browser.cjs</span>. הוא מריץ קובצי Web קיימים ואינו יוצר ביילד. להשתמש בתיקיית תוצאות חדשה כדי לשמר את הראיות.</p>
<h2>תיעוד רשמי</h2>
<p>[1] <a href="https://docs.unity.com/en-us/engine/6000.6/manual/platform-specific/webgl/develop/webassembly-2023/wasm-2023-prereq">Unity 6.6: דרישות WebAssembly 2023</a></p>
<p>[2] <a href="https://itch.io/docs/creators/html5">itch.io: העלאת משחקי HTML5 והפעלתם</a></p>
<p>[3] <a href="https://docs.unity.com/en-us/engine/6000.0/manual/platform-specific/webgl/develop/memory">Unity Web: מבנה הזיכרון וה־heap</a></p>
<p class="note">סדר העבודה: תיקון החללית → פרופיל איכות וקווי מתאר → טעינות וגופנים → חלקיקים ומאגרים → בדיקות מכשירים וקביעת מינימום נתמך. אין צורך בביילד נוסף כדי לקרוא או להשתמש בדוח זה.</p>`));

const html=`<!doctype html><html lang="he" dir="rtl"><meta charset="utf-8"><style>
@page{size:148mm 250mm;margin:0}*{box-sizing:border-box}body{margin:0;background:white;color:#182a35;font:11.1pt/1.5 Arial,sans-serif}section{width:148mm;height:250mm;padding:11mm 12mm 12mm;break-after:page;position:relative}section:last-child{break-after:auto}header{font-size:8pt;font-weight:bold;direction:ltr;color:#426277;border-bottom:1px solid #ccd7dd;padding-bottom:3mm;margin-bottom:6mm}header span{float:right;direction:rtl}h1{font-size:22pt;line-height:1.18;margin:0 0 5mm;color:#163e55}h2{font-size:13pt;line-height:1.35;margin:5mm 0 2mm}p{margin:2.5mm 0}.subtitle{font-size:13pt}.muted,.caption,.note{color:#51616a}.caption{font-size:8.5pt}.note{font-size:9pt;line-height:1.5;border-top:1px solid #ccd7dd;padding-top:3mm;margin-top:4mm}.verdict{background:#edf3f6;padding:3mm 4mm;margin:4mm 0}.verdict h2{margin:1mm 0 2mm}.metric{display:flex;align-items:center;gap:4mm;border-bottom:1px solid #d9e2e6;padding:4mm 0}.metric b{direction:ltr;min-width:34mm;color:#163e55;font-size:18pt}.metric span{font-size:10.5pt}table{width:100%;border-collapse:collapse;font-size:9.4pt;line-height:1.4;margin:4mm 0}th{background:#edf3f6;font-size:9pt}td,th{text-align:right;vertical-align:top;padding:2.2mm 1.8mm;border-bottom:1px solid #ccd7dd}td:not(:first-child){direction:ltr;text-align:center;font-variant-numeric:tabular-nums}.code,.path{direction:ltr;text-align:left;font-size:8.4pt;color:#51616a;line-height:1.3;overflow-wrap:anywhere}.path{font-size:10pt}ul{padding-right:5mm;margin:3mm 0}li{margin:2mm 0}a{color:#165c86;text-decoration:underline}footer{position:absolute;bottom:5mm;left:12mm;right:12mm;border-top:1px solid #d9e2e6;padding-top:1.5mm;display:flex;justify-content:space-between;font-size:8pt;color:#637681}
</style><body>${pages.map((s,i)=>s.replace('</section>',`<footer><span>BackHome · דוח ביצועים</span><span>${i+1} / ${pages.length}</span></footer></section>`)).join('')}</body></html>`;
fs.mkdirSync(root+'/output/pdf',{recursive:true});
fs.writeFileSync(root+'/tmp/pdfs/performance-report.html',html);
(async()=>{const browser=await chromium.launch({channel:'msedge',headless:true});try{const page=await browser.newPage();await page.setContent(html,{waitUntil:'load'});await page.evaluate(()=>document.fonts.ready);const overflow=await page.locator('section').evaluateAll(ss=>ss.map((s,i)=>({page:i+1,bottom:Math.max(...[...s.children].filter(c=>c.tagName!=='FOOTER').map(c=>c.getBoundingClientRect().bottom)),footer:s.querySelector('footer').getBoundingClientRect().top})).filter(x=>x.bottom>x.footer-8));if(overflow.length)throw new Error('Page overflow '+JSON.stringify(overflow));await page.pdf({path:out,preferCSSPageSize:true,printBackground:true});console.log(JSON.stringify({out,pages:pages.length,overflow}));}finally{await browser.close()}})().catch(e=>{console.error(e);process.exitCode=1});
