using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using PixelFight;

// Автотест: быстрый бой (спин+рывки против ИИ), затем кампания (клавиша C).
// Проверяем: удары проходят, раунды/этапы идут, match/campaign завершаются, нет NaN/крашей.
static class TestMain
{
    [STAThread]
    static int Main()
    {
        // Дымовой тест общего движка: .lic-проект, Lua- и C#-скрипты, спавн, физика, рендер.
        try
        {
            var proj = LicProject.NewSample();
            // Do not read Lic/demo.lic here: that path belongs to the newer PixelEngine 2.0
            // demo and intentionally has a different schema/content. Keep this legacy fixture
            // isolated so the test can run beside both generations of projects.
            var legacyFixtureDirectory = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "PixelFightTests", Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(legacyFixtureDirectory);
            var legacyFixturePath = System.IO.Path.Combine(legacyFixtureDirectory, "legacy-demo.lic");
            proj.Save(legacyFixturePath);
            var back = LicProject.Load(legacyFixturePath);
            System.IO.Directory.Delete(legacyFixtureDirectory, recursive: true);
            if (back.scenes.Count != 1 || back.scripts.Count != 2) { Console.WriteLine("FAIL: legacy lic round-trip"); return 3; }
            foreach (var s in back.scripts)
            {
                string err = ScriptFactory.CheckCode(s.lang, s.code);
                if (err != "") { Console.WriteLine("FAIL: script " + s.name + ": " + err); return 3; }
            }
            var rt = new Runtime();
            rt.Start(back, 0);
            var k2 = new HashSet<Keys>();
            using var bmp2 = new Bitmap(480, 270);
            using var gfx2 = Graphics.FromImage(bmp2);
            float heroX0 = rt.FindProxy("hero").x;
            for (int i = 0; i < 300; i++)
            {
                k2.Clear();
                k2.Add(Keys.D); // герой идёт вправо по Lua-скрипту
                if (i == 100) k2.Add(Keys.F); // выстрел -> spawn пули
                if (i % 50 == 0) k2.Add(Keys.W); // прыжки
                rt.Update(k2, 1f / 60f);
                if (i % 30 == 0) rt.Render(gfx2, false);
            }
            float heroX1 = rt.FindProxy("hero").x;
            bool pew = rt.logLines.Exists(l => l.Contains("pew"));
            bool enemyMoved = System.Math.Abs(rt.FindProxy("enemy").x - 60) > 5;
            Console.WriteLine($"engine: heroMoved={heroX1 - heroX0:F1} pew={pew} enemyMoved={enemyMoved}");
            if (heroX1 - heroX0 < 20) { Console.WriteLine("FAIL: lua hero не двигается"); return 3; }
            if (!pew) { Console.WriteLine("FAIL: lua spawn не сработал"); return 3; }
            if (!enemyMoved) { Console.WriteLine("FAIL: csharp enemy не двигается"); return 3; }
            Console.WriteLine("ENGINE PASS");
            // Стратегия Ages: ходы кнопкой, выбор и набор кликами, Lua без ошибок
            var strat = StrategyGame.Build();
            if (!System.IO.File.Exists("Lic/strategy.lic")) strat.Save("Lic/strategy.lic");
            string serr = ScriptFactory.CheckCode("lua", strat.scripts[0].code);
            if (serr != "") { Console.WriteLine("FAIL: ages.lua: " + serr); return 3; }
            var srt = new Runtime();
            srt.Start(StrategyGame.Build(), 0);
            var k3 = new HashSet<Keys>();
            using var bmp3 = new Bitmap(480, 270);
            using var gfx3 = Graphics.FromImage(bmp3);
            void Click(Runtime r, float wx, float wy)
            {
                r.Update(k3, 1f / 60f, wx, wy, true);
                r.Update(k3, 1f / 60f, wx, wy, false);
            }
            for (int t = 0; t < 3; t++)
            {
                for (int i = 0; i < 30; i++) srt.Update(k3, 1f / 60f);
                Click(srt, StrategyGame.BTurnX, StrategyGame.BtnY); // ХОД
            }
            bool turn3 = srt.logLines.Exists(l => l.Contains("ход 4"));
            Click(srt, 9, -18); // Тверь (Русь) — выбрать
            for (int i = 0; i < 5; i++) srt.Update(k3, 1f / 60f);
            float before = srt.FindProxy("p5").num;
            Click(srt, StrategyGame.BRecX, StrategyGame.BtnY); // НАБОР
            for (int i = 0; i < 5; i++) srt.Update(k3, 1f / 60f);
            float after = srt.FindProxy("p5").num;
            srt.Render(gfx3, true);
            Console.WriteLine($"ages: turn3={turn3} recruit {before:F0}->{after:F0}");
            if (!turn3) { Console.WriteLine("FAIL: ходы не идут"); return 3; }
            if (after - before < 100) { Console.WriteLine("FAIL: набор не сработал"); return 3; }
            Console.WriteLine("AGES PASS");
            // Мир с GitHub: загрузка, выбор страны кликом, ходы, рендер карты
            if (!System.IO.File.Exists("Lic/world.lic")) { Console.WriteLine("FAIL: нет world.lic"); return 3; }
            var wproj = LicProject.Load("Lic/world.lic");
            string werr = ScriptFactory.CheckCode("lua", wproj.scripts[0].code);
            if (werr != "") { Console.WriteLine("FAIL: world.lua: " + werr); return 3; }
            var wrt = new Runtime();
            wrt.Start(wproj, 0);
            var k4 = new HashSet<Keys>();
            void WClick(float wx, float wy)
            {
                wrt.Update(k4, 1f / 60f, wx, wy, true);
                wrt.Update(k4, 1f / 60f, wx, wy, false);
            }
            for (int i = 0; i < 30; i++) wrt.Update(k4, 1f / 60f);
            WClick(15, -14); // Москва — выбор России
            for (int i = 0; i < 5; i++) wrt.Update(k4, 1f / 60f);
            bool meRus = wrt.logLines.Exists(l => l.StartsWith("me="));
            WClick(-31, -37); // НАБОР (сразу: Москва точно наша, ИИ ещё не ходил)
            for (int i = 0; i < 5; i++) wrt.Update(k4, 1f / 60f);
            bool hired = wrt.logLines.Exists(l => l.Contains("набор +100"));
            for (int t = 0; t < 3; t++)
            {
                WClick(-59, -37); // ХОД
                for (int i = 0; i < 60; i++) wrt.Update(k4, 1f / 60f);
            }
            bool turn2 = wrt.logLines.Exists(l => l.Contains("ход 2"));
            bool werr2 = false;
            foreach (var o in wrt.objs) if (o.error != "") { Console.WriteLine("script err " + o.def.name + ": " + o.error); werr2 = true; }
            using (var wbmp = new Bitmap(480, 270))
            {
                using var wgfx = Graphics.FromImage(wbmp);
                wrt.Render(wgfx, false);
                wbmp.Save("Shots/world_preview.png", System.Drawing.Imaging.ImageFormat.Png);
            }
            Console.WriteLine($"world: стран={wproj.scenes[0].objects.Count / 2} me={meRus} turn2={turn2} hired={hired}");
            if (!meRus) { Console.WriteLine("FAIL: выбор страны"); return 3; }
            if (!turn2) { Console.WriteLine("FAIL: ход не идёт"); return 3; }
            if (!hired) { Console.WriteLine("FAIL: набор не сработал"); return 3; }
            if (werr2) return 3;
            Console.WriteLine("WORLD PASS");
            // Купец: выбор города и товара, покупка, месяц, продажа
            var mproj = MerchantGame.Build();
            if (!System.IO.File.Exists("Lic/merchant.lic")) mproj.Save("Lic/merchant.lic");
            string merr = ScriptFactory.CheckCode("lua", mproj.scripts[0].code);
            if (merr != "") { Console.WriteLine("FAIL: market.lua: " + merr); return 3; }
            var mrt = new Runtime();
            mrt.Start(MerchantGame.Build(), 0);
            var k5 = new HashSet<Keys>();
            void MClick(float wx, float wy)
            {
                mrt.Update(k5, 1f / 60f, wx, wy, true);
                mrt.Update(k5, 1f / 60f, wx, wy, false);
            }
            for (int i = 0; i < 10; i++) mrt.Update(k5, 1f / 60f);
            MClick(-64, -20); // Новгород
            MClick(0, 20); // строка 1 (Хлеб)
            MClick(MerchantGame.BXBuy, MerchantGame.BtnY); // КУПИТЬ
            for (int i = 0; i < 5; i++) mrt.Update(k5, 1f / 60f);
            bool bought = mrt.logLines.Exists(l => l.StartsWith("куплено"));
            MClick(MerchantGame.BXTurn, MerchantGame.BtnY); // МЕСЯЦ
            for (int i = 0; i < 5; i++) mrt.Update(k5, 1f / 60f);
            bool month2 = mrt.logLines.Exists(l => l.Contains("месяц 2"));
            MClick(MerchantGame.BXSell, MerchantGame.BtnY); // ПРОДАТЬ
            for (int i = 0; i < 5; i++) mrt.Update(k5, 1f / 60f);
            bool sold = mrt.logLines.Exists(l => l.StartsWith("продано"));
            bool merr2 = false;
            foreach (var o in mrt.objs) if (o.error != "") { Console.WriteLine("script err " + o.def.name + ": " + o.error); merr2 = true; }
            using (var mbmp = new Bitmap(480, 270))
            {
                using var mgfx = Graphics.FromImage(mbmp);
                mrt.Render(mgfx, false);
                mbmp.Save("Shots/merchant_preview.png", System.Drawing.Imaging.ImageFormat.Png);
            }
            Console.WriteLine($"merchant: bought={bought} month2={month2} sold={sold}");
            if (!bought || !month2 || !sold || merr2) { Console.WriteLine("FAIL: купец"); return 3; }
            Console.WriteLine("MERCHANT PASS");
            // Оборона: ставим 2 лука у старта, ждём фраги и волну 2
            var dproj = TowerDefense.Build();
            if (!System.IO.File.Exists("Lic/tower.lic")) dproj.Save("Lic/tower.lic");
            string derr = ScriptFactory.CheckCode("lua", dproj.scripts[0].code);
            if (derr != "") { Console.WriteLine("FAIL: defense.lua: " + derr); return 3; }
            var drt = new Runtime();
            drt.Start(TowerDefense.Build(), 0);
            var k6 = new HashSet<Keys>();
            void DClick(float wx, float wy)
            {
                drt.Update(k6, 1f / 60f, wx, wy, true);
                drt.Update(k6, 1f / 60f, wx, wy, false);
            }
            for (int i = 0; i < 10; i++) drt.Update(k6, 1f / 60f);
            DClick(-55, -18); // лук у старта
            DClick(-55, -8); // второй лук
            for (int i = 0; i < 60 * 40; i++) drt.Update(k6, 1f / 60f);
            int kills = 0;
            foreach (var l in drt.logLines) if (l.StartsWith("убит")) kills++;
            bool wave2 = drt.logLines.Exists(l => l.Contains("волна 2"));
            bool derr2 = false;
            foreach (var o in drt.objs) if (o.error != "") { Console.WriteLine("script err " + o.def.name + ": " + o.error); derr2 = true; }
            using (var dbmp = new Bitmap(480, 270))
            {
                using var dgfx = Graphics.FromImage(dbmp);
                drt.Render(dgfx, false);
                dbmp.Save("Shots/tower_preview.png", System.Drawing.Imaging.ImageFormat.Png);
            }
            Console.WriteLine($"tower: kills={kills} wave2={wave2}");
            if (kills < 3 || !wave2 || derr2) { Console.WriteLine("FAIL: оборона"); return 3; }
            Console.WriteLine("TOWER PASS");
            // Рыцари: выпускаем 4 вида, ждём фраги
            var cproj = KnightsDefense.Build();
            if (!System.IO.File.Exists("Lic/knights.lic")) cproj.Save("Lic/knights.lic");
            string cerr = ScriptFactory.CheckCode("lua", cproj.scripts[0].code);
            if (cerr != "") { Console.WriteLine("FAIL: knights.lua: " + cerr); return 3; }
            var crt = new Runtime();
            crt.Start(KnightsDefense.Build(), 0);
            var k7 = new HashSet<Keys>();
            void CClick(float wx, float wy)
            {
                crt.Update(k7, 1f / 60f, wx, wy, true);
                crt.Update(k7, 1f / 60f, wx, wy, false);
            }
            Store.Set("knights_level", "1");
            Store.Set("knights_max", "1");
            for (int i = 0; i < 10; i++) crt.Update(k7, 1f / 60f);
            CClick(-33, -22); // ИГРАТЬ из меню
            for (int i = 0; i < 5; i++) crt.Update(k7, 1f / 60f);
            bool playGo = crt.logLines.Exists(l => l.StartsWith("игра!"));
            CClick(KnightsDefense.BX1, KnightsDefense.BtnY);
            CClick(KnightsDefense.BX2, KnightsDefense.BtnY);
            CClick(KnightsDefense.BX3, KnightsDefense.BtnY);
            CClick(KnightsDefense.BX4, KnightsDefense.BtnY);
            CClick(-57, 27); // ОГОНЬ: вооружить
            CClick(40, -6); // ОГОНЬ: цель по врагу
            for (int i = 0; i < 60 * 45; i++) crt.Update(k7, 1f / 60f);
            float frags = crt.FindProxy("gm").num;
            bool cerr2 = false;
            foreach (var o in crt.objs) if (o.error != "") { Console.WriteLine("script err " + o.def.name + ": " + o.error); cerr2 = true; }
            using (var cbmp = new Bitmap(480, 270))
            {
                using var cgfx = Graphics.FromImage(cbmp);
                crt.Render(cgfx, false);
                cbmp.Save("Shots/knights_preview.png", System.Drawing.Imaging.ImageFormat.Png);
            }
            Console.WriteLine($"knights: frags={frags}");
            if (frags < 1 || cerr2) { Console.WriteLine("FAIL: рыцари"); return 3; }
            if (!playGo) { Console.WriteLine("FAIL: меню игры"); return 3; }
            if (!crt.logLines.Exists(l => l.Contains("огонь!"))) { Console.WriteLine("FAIL: заклинание"); return 3; }
            // сетка уровней на чистом рантайме: меню -> УРОВНИ -> клетка 1 -> игра
            {
                var grt = new Runtime();
                grt.Start(KnightsDefense.Build(), 0);
                void GClick(float wx, float wy)
                {
                    grt.Update(k7, 1f / 60f, wx, wy, true);
                    grt.Update(k7, 1f / 60f, wx, wy, false);
                }
                for (int i = 0; i < 10; i++) grt.Update(k7, 1f / 60f);
                using (var gbmp = new Bitmap(480, 270))
                {
                    using var ggfx = Graphics.FromImage(gbmp);
                    grt.Render(ggfx, false);
                    gbmp.Save("Shots/menu_preview.png", System.Drawing.Imaging.ImageFormat.Png);
                }
                GClick(33, -22); // УРОВНИ
                for (int i = 0; i < 5; i++) grt.Update(k7, 1f / 60f);
                GClick(-63, -30); // клетка 1
                for (int i = 0; i < 5; i++) grt.Update(k7, 1f / 60f);
                if (!grt.logLines.Exists(l => l == "игра! ур.1")) { Console.WriteLine("FAIL: сетка уровней"); return 3; }
            }
            // меню чистое: после боя выход в МЕНЮ — ни трупов, ни юнитов, ни частиц поверх
            {
                CClick(16, 20); // МЕНЮ из боя
                for (int i = 0; i < 5; i++) crt.Update(k7, 1f / 60f);
                for (int i = 0; i < 120; i++) crt.Update(k7, 1f / 60f); // трупы истлели бы и без purge
                int leftover = 0;
                foreach (var o in crt.objs)
                    if (o.def.name == "kat" || o.def.name == "foe" || o.def.name == "boss" || o.def.name == "hero" || o.def.name == "pt")
                        leftover++;
                using (var mbmp = new Bitmap(480, 270))
                {
                    using var mgfx = Graphics.FromImage(mbmp);
                    crt.Render(mgfx, false);
                }
                Console.WriteLine($"menu-clean: leftover={leftover}");
                if (leftover != 0) { Console.WriteLine("FAIL: мусор поверх меню"); return 3; }
            }
            Store.Set("t", "42");
            if (Store.Get("t") != "42" || Store.Get("nope", "d") != "d") { Console.WriteLine("FAIL: хранилище"); return 3; }
            Console.WriteLine("KNIGHTS PASS");
            // Все кнопки: меню (сложность/помощь/уровни/сброс) + все 8 юнитов, заклинания,
            // доход/стена/урон/броня, пауза, скорость, выход в меню — без ошибок скрипта
            {
                var brt = new Runtime();
                brt.Start(KnightsDefense.Build(), 0);
                void BClick(float wx, float wy)
                {
                    brt.Update(k7, 1f / 60f, wx, wy, true);
                    brt.Update(k7, 1f / 60f, wx, wy, false);
                }
                for (int i = 0; i < 10; i++) brt.Update(k7, 1f / 60f);
                BClick(33, -8); // сложность
                BClick(-33, -8); // помощь
                for (int i = 0; i < 3; i++) brt.Update(k7, 1f / 60f);
                BClick(-14, 34); // назад в меню
                BClick(0, 5); // СЛУЧАЙНЫЙ уровень (макс открыт 1 → ур.1)
                for (int i = 0; i < 5; i++) brt.Update(k7, 1f / 60f);
                BClick(16, 20); // в меню из боя
                for (int i = 0; i < 5; i++) brt.Update(k7, 1f / 60f);
                BClick(33, -22); // уровни
                for (int i = 0; i < 3; i++) brt.Update(k7, 1f / 60f);
                BClick(32, 34); // страница 2: клетка 1 там закрыта
                for (int i = 0; i < 3; i++) brt.Update(k7, 1f / 60f);
                BClick(-63, -30); // клетка 51 — закрыта
                for (int i = 0; i < 3; i++) brt.Update(k7, 1f / 60f);
                BClick(32, 34); // назад на страницу 1
                BClick(-63, -30); // клетка 1
                for (int i = 0; i < 5; i++) brt.Update(k7, 1f / 60f);
                bool bDollar = (brt.FindProxy("ui_money")?.text ?? "").StartsWith("$");
                BClick(-12, 20); // ЗАЛП со стен (маны хватает)
                float[] ux8 = { -63, -45, -27, -9, 9, 27, 45, 63 };
                foreach (float ux in ux8) { BClick(ux, KnightsDefense.BtnY); for (int i = 0; i < 10; i++) brt.Update(k7, 1f / 60f); }
                BClick(-57, 27); // огонь: вооружить
                BClick(40, -6); // огонь: цель
                BClick(-34, 27); // лечение
                BClick(-11, 27); // доход
                BClick(11, 27); // стена
                k7.Add(Keys.U); brt.Update(k7, 1f / 60f); k7.Remove(Keys.U); // урон казармам
                k7.Add(Keys.I); brt.Update(k7, 1f / 60f); k7.Remove(Keys.I); // броня казармам
                k7.Add(Keys.V); brt.Update(k7, 1f / 60f); k7.Remove(Keys.V); // залп с клавиатуры
                BClick(57, 27); // скорость
                BClick(34, 27); // пауза вкл
                BClick(34, 27); // пауза выкл
                k7.Add(Keys.Space); brt.Update(k7, 1f / 60f); k7.Remove(Keys.Space); // Space = пауза
                k7.Add(Keys.F); brt.Update(k7, 1f / 60f); k7.Remove(Keys.F); // F = скорость
                k7.Add(Keys.Space); brt.Update(k7, 1f / 60f); k7.Remove(Keys.Space); // Space = снять паузу
                for (int i = 0; i < 5; i++) brt.Update(k7, 1f / 60f);
                BClick(16, 20); // в меню
                for (int i = 0; i < 5; i++) brt.Update(k7, 1f / 60f);
                BClick(0, 30); // сброс прогресса
                for (int i = 0; i < 3; i++) brt.Update(k7, 1f / 60f);
                bool berr = false;
                foreach (var o in brt.objs) if (o.error != "") { Console.WriteLine("script err " + o.def.name + ": " + o.error); berr = true; }
                bool bDiff = brt.logLines.Exists(l => l.StartsWith("сложность:"));
                bool bGame = brt.logLines.Exists(l => l == "игра! ур.1");
                bool bPause = brt.logLines.Exists(l => l == "пауза") && brt.logLines.Exists(l => l == "вперёд!");
                bool bWipe = brt.logLines.Exists(l => l.Contains("прогресс сброшен"));
                bool bLocked = brt.logLines.Exists(l => l.StartsWith("закрыто:"));
                bool bVolley = brt.logLines.Exists(l => l.Contains("Залп со стен!"));
                bool bKeys = brt.logLines.Exists(l => l == "скорость ×1");
                Console.WriteLine($"buttons: diff={bDiff} game={bGame} pause={bPause} wipe={bWipe} locked={bLocked} volley={bVolley} dollar={bDollar} keys={bKeys} err={berr}");
                if (berr || !bDiff || !bGame || !bPause || !bWipe || !bLocked || !bVolley || !bDollar || !bKeys) { Console.WriteLine("FAIL: кнопки"); return 3; }
                Store.Set("knights_level", "1");
                Store.Set("knights_max", "1");
                Store.Set("knights_diff", "2");
            }
            Console.WriteLine("BUTTONS PASS");
            // Запакованное = свежее: грузим game.lic с диска (то, что видит exe) — пауза/скорость обязаны работать
            {
                string ship = "Publish/Games/Knights/game.lic";
                if (!System.IO.File.Exists(ship)) { Console.WriteLine("FAIL: нет запаковки — запусти pack_games.ps1"); return 3; }
                var lproj = LicProject.Load(ship);
                var lrt = new Runtime();
                lrt.Start(lproj, 0);
                void LClick(float wx, float wy)
                {
                    lrt.Update(k7, 1f / 60f, wx, wy, true);
                    lrt.Update(k7, 1f / 60f, wx, wy, false);
                }
                for (int i = 0; i < 10; i++) lrt.Update(k7, 1f / 60f);
                bool lver = (lrt.FindProxy("subtitle")?.text ?? "").Contains("v7");
                LClick(-33, -22);
                for (int i = 0; i < 5; i++) lrt.Update(k7, 1f / 60f);
                LClick(34, 27);
                LClick(57, 27);
                k7.Add(Keys.Space); lrt.Update(k7, 1f / 60f); k7.Remove(Keys.Space);
                bool lerr = false;
                foreach (var o in lrt.objs) if (o.error != "") { Console.WriteLine("script err " + o.def.name + ": " + o.error); lerr = true; }
                bool lPlay = lrt.logLines.Exists(l => l.StartsWith("игра!"));
                bool lPause = lrt.logLines.Exists(l => l == "пауза");
                bool lFast = lrt.logLines.Exists(l => l == "скорость ×2");
                Console.WriteLine($"shipped: v7={lver} play={lPlay} pause={lPause} fast={lFast} err={lerr}");
                if (lerr || !lver || !lPlay || !lPause || !lFast) { Console.WriteLine("FAIL: запаковка старая/сломана"); return 3; }
            }
            Console.WriteLine("SHIPPED PASS");
            // Победа: агрессивная игра до конца — экран со звёздами, кнопка ДАЛЕЕ ведёт на ур.2
            {
                Store.Set("knights_level", "1");
                Store.Set("knights_max", "1");
                var krt = new Runtime();
                krt.Start(KnightsDefense.Build(), 0);
                void KClick(float wx, float wy)
                {
                    krt.Update(k7, 1f / 60f, wx, wy, true);
                    krt.Update(k7, 1f / 60f, wx, wy, false);
                }
                for (int i = 0; i < 10; i++) krt.Update(k7, 1f / 60f);
                KClick(-33, -22);
                for (int i = 0; i < 5; i++) krt.Update(k7, 1f / 60f);
                KClick(57, 27); // скорость ×2 — быстрее к победе
                float[] dep = { -63, -27, -45, -9, 9, -63, -27, -45 };
                int di = 0, wonAt = -1;
                for (int i = 0; i < 60 * 220 && wonAt < 0; i++)
                {
                    if (i % 45 == 0) { KClick(dep[di % dep.Length], KnightsDefense.BtnY); di++; }
                    if (i % 400 == 200) { KClick(-57, 27); KClick(40, -6); } // огонь по врагу
                    if (i % 700 == 350) KClick(-12, 20); // залп
                    if (i % 900 == 450) { KClick(-11, 27); KClick(11, 27); } // доход+стена
                    else krt.Update(k7, 1f / 60f);
                    if (krt.logLines.Exists(l => l.StartsWith("win!"))) wonAt = i;
                }
                bool kerr = false;
                foreach (var o in krt.objs) if (o.error != "") { Console.WriteLine("script err " + o.def.name + ": " + o.error); kerr = true; }
                bool wStars = (krt.FindProxy("win_stars")?.text ?? "").Length > 0;
                bool wNext = System.Math.Abs((krt.FindProxy("win_next")?.y ?? -200) - 9) < 0.5;
                using (var wfin = new Bitmap(480, 270))
                {
                    using var wgfx = Graphics.FromImage(wfin);
                    krt.Render(wgfx, false);
                    wfin.Save("Shots/win_preview.png", System.Drawing.Imaging.ImageFormat.Png);
                }
                Console.WriteLine($"win: won={wonAt >= 0} stars={wStars} next={wNext} err={kerr}");
                if (kerr || wonAt < 0 || !wStars || !wNext) { Console.WriteLine("FAIL: победа"); return 3; }
                KClick(0, 9); // ДАЛЕЕ
                for (int i = 0; i < 5; i++) krt.Update(k7, 1f / 60f);
                if (!krt.logLines.Exists(l => l == "Уровень 2: Зелёные поля")) { Console.WriteLine("FAIL: далее"); return 3; }
                Store.Set("knights_level", "1");
                Store.Set("knights_max", "1");
            }
            Console.WriteLine("WIN PASS");
            // Ночная витрина (ур.4): факелы, сапёр, трупы, новые кнопки — один кадр для глаз
            {
                Store.Set("knights_level", "4");
                Store.Set("knights_max", "4");
                var nrt = new Runtime();
                nrt.Start(KnightsDefense.Build(), 0);
                void NClick(float wx, float wy)
                {
                    nrt.Update(k7, 1f / 60f, wx, wy, true);
                    nrt.Update(k7, 1f / 60f, wx, wy, false);
                }
                for (int i = 0; i < 10; i++) nrt.Update(k7, 1f / 60f);
                NClick(-40, -20); // играть (ур.4 — ночь)
                for (int i = 0; i < 5; i++) nrt.Update(k7, 1f / 60f);
                NClick(-63, KnightsDefense.BtnY); // оруженосец
                NClick(-9, KnightsDefense.BtnY); // копейщик
                NClick(63, KnightsDefense.BtnY); // сапёр
                NClick(-57, 27); // огонь: вооружить
                NClick(40, -6); // огонь: цель
                for (int i = 0; i < 60 * 35; i++) nrt.Update(k7, 1f / 60f);
                bool nerr = false;
                foreach (var o in nrt.objs) if (o.error != "") { Console.WriteLine("script err " + o.def.name + ": " + o.error); nerr = true; }
                using (var nbmp = new Bitmap(480, 270))
                {
                    using var ngfx = Graphics.FromImage(nbmp);
                    nrt.Render(ngfx, false);
                    nbmp.Save("Shots/knights_showcase.png", System.Drawing.Imaging.ImageFormat.Png);
                }
                Console.WriteLine($"night: night={nrt.logLines.Exists(l => l.Contains("ночь"))} err={nerr}");
                if (nerr) { Console.WriteLine("FAIL: ночь"); return 3; }
                Store.Set("knights_level", "1");
                Store.Set("knights_max", "1");
            }
            Console.WriteLine("SHOWCASE PASS");
            // Витрина: каждая внешность/строение рендерится без падений
            {
                var castp = new LicProject { name = "Cast" };
                var castsc = new LicScene { name = "S", gravity = 0, groundY = 0, lockCam = true, camX0 = 0, camY0 = -4, deco = true, bg1 = "#8FC3E8", bg2 = "#D8EBD8" };
                string[] looks = { "squire", "sword", "pala", "xbow", "spear", "sapper", "monk", "hero", "cata", "bandit", "dog", "boar", "king", "ram", "siege" };
                for (int i = 0; i < looks.Length; i++)
                    castsc.objects.Add(new LicObject { name = "k" + i, kind = "knight", x = -70 + i * 11, y = -6, w = 9, h = 13, color = i % 2 == 0 ? "#3E589E" : "#9E3A3A", gravity = false, solid = false, sprite = looks[i] });
                castsc.objects.Add(new LicObject { name = "cs", kind = "castle", x = -40, y = -15, w = 16, h = 30, color = "#3E589E", gravity = false, solid = false });
                castsc.objects.Add(new LicObject { name = "cp", kind = "camp", x = 40, y = -13, w = 22, h = 26, color = "#9E3A3A", gravity = false, solid = false });
                castsc.objects.Add(new LicObject { name = "tw", kind = "tower", x = 0, y = -7, w = 9, h = 14, color = "#888888", gravity = false, solid = false });
                castp.scenes.Add(castsc);
                var castrt = new Runtime();
                castrt.Start(castp, 0);
                var castk = new HashSet<Keys>();
                for (int i = 0; i < 30; i++) castrt.Update(castk, 1f / 60f);
                using var castbmp = new Bitmap(480, 270);
                using (var castg = Graphics.FromImage(castbmp)) { castrt.Render(castg, false); castbmp.Save("Shots/cast_preview.png", System.Drawing.Imaging.ImageFormat.Png); }
            }
            Console.WriteLine("CAST PASS");
            // Вдвоём: J меняет оружие P2; повтор детерминирован (600 кадров = тот же стейт)
            var vg = new Game();
            var vk = new HashSet<Keys> { Keys.V };
            vg.Update(vk, new Point(240, 135), false, false, 1f / 60f); // меню -> Versus
            vk.Clear();
            for (int i = 0; i < 30; i++) vg.Update(vk, new Point(240, 135), false, false, 1f / 60f);
            string w0 = vg.GetState().Split('|')[13];
            vk.Add(Keys.J);
            vg.Update(vk, new Point(240, 135), false, false, 1f / 60f);
            vk.Clear();
            vg.Update(vk, new Point(240, 135), false, false, 1f / 60f);
            string w1 = vg.GetState().Split('|')[13];
            Console.WriteLine($"versus: p2weapon {w0}->{w1}");
            if (w0 == w1) { Console.WriteLine("FAIL: J не меняет оружие P2"); return 3; }
            var rg = new Game();
            var rk = new HashSet<Keys>();
            int ff = 0;
            for (int i = 0; i < 600; i++)
            {
                rk.Clear();
                if (i == 5) rk.Add(Keys.Enter);
                string st = rg.GetState();
                var pp = st.Split('|');
                if (pp[0] == "1")
                {
                    ff++;
                    int px = int.Parse(pp[4].Split(',')[1]), ex = int.Parse(pp[5].Split(',')[1]);
                    if (ex > px + 5) rk.Add(Keys.D); else if (ex < px - 5) rk.Add(Keys.A);
                    double ang = i * 0.35;
                    double cam = double.Parse(pp[6]);
                    rk.Add(Keys.W);
                    rg.Update(rk, new Point((int)((px + Math.Cos(ang) * 55 - cam) * 3 + 240), 135), false, false, 1f / 60f);
                }
                else rg.Update(rk, new Point(240, 135), false, false, 1f / 60f);
            }
            string s1 = rg.GetState();
            if (!rg.HasReplay) { Console.WriteLine("FAIL: нет записи повтора"); return 3; }
            rg.PlayReplay();
            var empty = new HashSet<Keys>();
            for (int i = 0; i < ff; i++) rg.Update(empty, new Point(0, 0), false, false, 1f / 60f);
            string s2 = rg.GetState();
            Console.WriteLine($"replay: match={s1 == s2}");
            if (s1 != s2) { Console.WriteLine("FAIL: повтор расходится"); Console.WriteLine("A " + s1); Console.WriteLine("B " + s2); return 3; }
            rg.PlayReplay();
            for (int i = 0; i < 50; i++) rg.Update(empty, new Point(0, 0), false, false, 1f / 60f);
            var esc = new HashSet<Keys> { Keys.Escape };
            rg.Update(esc, new Point(0, 0), false, false, 1f / 60f);
            string s3 = rg.GetState();
            Console.WriteLine($"replay exit: state={s3.Split('|')[0]} playing={rg.Replaying}");
            if (!s3.StartsWith("2|") || rg.Replaying) { Console.WriteLine("FAIL: нет выхода из повтора"); return 3; }
            Console.WriteLine("FIGHT PASS");
            // Тренировка и выживание: старт по T/Y, бой идёт, рендер цел
            foreach (var key in new[] { Keys.T, Keys.Y })
            {
                var mg = new Game();
                var mk = new HashSet<Keys> { key };
                mg.Update(mk, new Point(240, 135), false, false, 1f / 60f);
                mk.Clear();
                mk.Add(Keys.D);
                using var mbmp = new Bitmap(480, 270);
                using var mgfx = Graphics.FromImage(mbmp);
                string mst = "";
                for (int i = 0; i < 200; i++)
                {
                    double ang = i * 0.3;
                    mg.Update(mk, new Point((int)(240 + Math.Cos(ang) * 60), 135), false, false, 1f / 60f);
                    if (i % 50 == 0) mg.Render(mgfx);
                    mst = mg.GetState();
                }
                var mp = mst.Split('|');
                Console.WriteLine($"mode={mp[9]} state={mp[0]}");
                int want = key == Keys.T ? 3 : 4;
                if (mp[9] != want.ToString() || (mp[0] != "1" && mp[0] != "2")) { Console.WriteLine("FAIL: режим"); return 3; }
            }
            // Лук: авто-огонь без падений
            {
                var bg = new Game();
                bg.SetPlayerWeapon(8);
                var bk = new HashSet<Keys> { Keys.Enter };
                bg.Update(bk, new Point(240, 135), false, false, 1f / 60f);
                bk.Clear();
                using var bbmp = new Bitmap(480, 270);
                using var bgfx = Graphics.FromImage(bbmp);
                for (int i = 0; i < 120; i++)
                {
                    bg.Update(bk, new Point(200 + i % 80, 135), false, false, 1f / 60f);
                    if (i % 30 == 0) bg.Render(bgfx);
                }
                if (!bg.GetState().StartsWith("1|")) { Console.WriteLine("FAIL: лук"); return 3; }
            }
            Console.WriteLine("MODES PASS");
            var sg = new Game();
            var sk = new HashSet<Keys>();
            for (int i = 0; i < 5; i++) sg.Update(sk, new Point(334, 244), false, false, 1f / 60f);
            sg.Update(sk, new Point(334, 244), true, false, 1f / 60f); // клик МАГАЗИН
            sg.Update(sk, new Point(334, 244), false, false, 1f / 60f);
            using (var sbmp = new Bitmap(480, 270))
            {
                using var sgfx = Graphics.FromImage(sbmp);
                sg.Render(sgfx);
                sbmp.Save("Shots/fight_shop.png", System.Drawing.Imaging.ImageFormat.Png);
            }
            Console.WriteLine("SHOP SHOT");
            var cg = new Game();
            var ck = new HashSet<Keys> { Keys.C };
            for (int i = 0; i < 5; i++) cg.Update(ck, new Point(240, 135), false, false, 1f / 60f); // меню -> страны
            ck.Clear();
            for (int i = 0; i < 5; i++) cg.Update(ck, new Point(240, 135), false, false, 1f / 60f);
            using (var cbmp = new Bitmap(480, 270))
            {
                using var cgfx = Graphics.FromImage(cbmp);
                cg.Render(cgfx);
                cbmp.Save("Shots/fight_countries.png", System.Drawing.Imaging.ImageFormat.Png);
            }
            Console.WriteLine("COUNTRIES SHOT");
        }
        catch (Exception ex)
        {
            Console.WriteLine("ENGINE EXCEPTION: " + ex);
            return 3;
        }
        var game = new Game();
        var keys = new HashSet<Keys>();
        bool enemyDamaged = false, playerDamaged = false, sawFight = false;
        bool sawCamp = false, campAdvanced = false, bad = false;
        bool quickDone = false, campStarted = false;
        bool shotFight = false;
        int maxDrops = 0;
        string last = "";
                int frames = 60 * 190;
        using var bmp = new Bitmap(480, 270);
        using var gfx = Graphics.FromImage(bmp);
        try
        {
            for (int i = 0; i < frames; i++)
            {
                keys.Clear();
                if (i == 5) keys.Add(Keys.Enter); // старт быстрого боя из меню
                string st = game.GetState();
                last = st;
                var p = st.Split('|');
                int state = int.Parse(p[0]); // 0 меню, 1 бой, 2 конец
                int mode = int.Parse(p[9]), stage = int.Parse(p[10]);
                if (p.Length > 11) maxDrops = Math.Max(maxDrops, int.Parse(p[11]));
                if (state == 2 && !campStarted)
                {
                    if (mode == 0) quickDone = true;
                    keys.Add(Keys.Enter); // из конца быстрого матча — в меню
                }
                else if (state == 0 && quickDone && !campStarted) { keys.Add(Keys.C); campStarted = true; }
                else if (state == 2 && campStarted) keys.Add(Keys.R); // проигрыш этапа — повтор
                Point mouse = new Point(240, 135);
                bool lmb = false, rmb = false;
                if (state == 6) { mouse = new Point(100, 104); lmb = (i % 60 == 0); } // клик по Японии
                else if (state == 3) { mouse = new Point(64, 150); lmb = (i % 60 == 0); } // клик по 1-му этапу
                else if (state == 1)
                {
                    sawFight = true;
                    if (mode == 1) sawCamp = true;
                    var pp = p[4].Split(','); var ee = p[5].Split(',');
                    int php = int.Parse(pp[0]), px = int.Parse(pp[1]), py = int.Parse(pp[2]);
                    int ehp = int.Parse(ee[0]), ex = int.Parse(ee[1]);
                    if (ehp < 100) enemyDamaged = true;
                    if (php < 100) playerDamaged = true;
                    if (mode == 1 && stage > 0) campAdvanced = true;
                    if (Math.Abs(px) > 10000 || Math.Abs(ex) > 10000) bad = true;
                    // идём на врага как живой игрок + крутим меч
                    if (ex > px + 5) keys.Add(Keys.D);
                    else if (ex < px - 5) keys.Add(Keys.A);
                    if (i % 300 < 8) keys.Add(Keys.W); // прыжки
                    double cam = double.Parse(p[6]);
                    double ang = i * 0.35; // быстрый спин курсором
                    double wx = px + Math.Cos(ang) * 55, wy = (py - 20) + Math.Sin(ang) * 55;
                    mouse = new Point((int)((wx - cam) * 3 + 240), (int)((wy + 40) * 3 + 135));
                    if (i % 120 < 6) lmb = true; // рывки
                }
                game.Update(keys, mouse, lmb, rmb, 1f / 60f);
                if (i % 10 == 0) game.Render(gfx); // и рендер не должен падать
                if (i == 2) bmp.Save("Shots/fight_menu.png", System.Drawing.Imaging.ImageFormat.Png);
                if (state == 1 && !shotFight && i > 300) { bmp.Save("Shots/fight_fight.png", System.Drawing.Imaging.ImageFormat.Png); shotFight = true; }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("EXCEPTION: " + ex);
            Console.WriteLine("LAST STATE: " + last);
            return 2;
        }
        var fin = last.Split('|');
        var fp = fin[4].Split(','); var fe = fin[5].Split(',');
        Console.WriteLine($"sawFight={sawFight} enemyDamaged={enemyDamaged} playerDamaged={playerDamaged} badPos={bad}");
        Console.WriteLine($"final: state={fin[0]} score={fin[1]}:{fin[2]} round={fin[3]} php={fp[0]} ehp={fe[0]} mode={fin[9]} stage={fin[10]}");
        Console.WriteLine($"sawCamp={sawCamp} campAdvanced={campAdvanced} maxDropsOnGround={maxDrops}");
        if (!sawFight) { Console.WriteLine("FAIL: бой не начался"); return 1; }
        if (bad) { Console.WriteLine("FAIL: разъехавшиеся координаты"); return 1; }
        if (!enemyDamaged) { Console.WriteLine("FAIL: спин не нанёс урона"); return 1; }
        if (!sawCamp) { Console.WriteLine("FAIL: кампания не запустилась"); return 1; }
        if (!campAdvanced) { Console.WriteLine("FAIL: этапы кампании не переключаются"); return 1; }
        Console.WriteLine("PASS");
        return 0;
    }
}
