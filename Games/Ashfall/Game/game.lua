-- ASHFALL: THE LAST BEACON
-- PixelEngine 2.0 / Stage 11
-- Controls: WASD or arrows move | mouse aim | hold J/F or click to fire
-- SHIFT / SPACE dash | Q nova pulse | P pause | 1/2/3 choose upgrades

local state = "menu"
local player = nil
local enemies = {}
local bullets = {}
local enemyShots = {}
local drops = {}
local effects = {}
local ui = {}

local wave = 1
local score = 0
local bestScore = 0
local kills = 0
local level = 1
local xp = 0
local xpNeed = 14
local hp = 100
local maxHp = 100
local damage = 18
local fireDelay = 0.23
local moveSpeed = 29
local shotSpeed = 76
local fireTimer = 0
local dashTimer = 0
local dashCooldown = 0
local invulnerable = 0
local pulseCooldown = 0
local spawnLeft = 0
local spawnTimer = 0
local intermissionTimer = 0
local bossSpawned = false
local boss = nil
local hudTimer = 0
local noticeTimer = 0
local noticeText = ""
local lastDirX = 1
local lastDirY = 0
local gameClock = 0
local upgradeChoices = {}
local upgradeUi = {}
local removeUpgradeUi = nil

local ARENA_X = 68
local ARENA_Y = 27
local PLAYER_RADIUS = 3.6

local function clamp(v, lo, hi)
    if v < lo then return lo end
    if v > hi then return hi end
    return v
end

local function distance(ax, ay, bx, by)
    local dx = ax - bx
    local dy = ay - by
    return math.sqrt(dx * dx + dy * dy)
end

local function makeText(name, x, y, value, color)
    return api.spawn("text", {
        name = name, x = x, y = y, w = 100, h = 4,
        text = value or "", color = color or "#FFFFFF",
        gravity = false, solid = false
    })
end

local function removeNamed(name)
    local item = api.find(name)
    if item ~= nil then item.destroy() end
end

local function destroyList(list)
    for i = #list, 1, -1 do
        local item = list[i]
        if item ~= nil and item.o ~= nil and not item.o.dead then
            item.o.destroy()
        end
        table.remove(list, i)
    end
end

local function notice(message, color)
    noticeText = message or ""
    noticeTimer = 2.0
    if ui.notice ~= nil then
        ui.notice.text = noticeText
        ui.notice.color = color or "#B9F7FF"
    end
end

local function bindControls()
    api.bind_key("dash", "LeftShift")
    api.bind_button("dash", "B")
    api.bind_key("fire", "J")
    api.bind_button("fire", "RightShoulder")
    api.bind_key("pulse", "Q")
    api.bind_button("pulse", "Y")
    api.bind_key("pause", "P")
    api.bind_button("pause", "Start")
end

local function createHud()
    ui.header = makeText("ash_hud_header", 0, -41, "WAVE 1/5   SCORE 000000   LV 1", "#D9F8FF")
    ui.hpText = makeText("ash_hud_hp", -56, -35, "HULL 100/100", "#8DFFE0")
    ui.abilityText = makeText("ash_hud_ability", 48, -35, "DASH READY  NOVA READY", "#A8B6FF")
    ui.hpBack = api.spawn("rect", {name="ash_hp_back", x=-28, y=-30.5, w=42, h=1.5, color="#24394B", gravity=false, solid=false})
    ui.hpFill = api.spawn("rect", {name="ash_hp_fill", x=-49, y=-30.5, w=42, h=1.1, color="#48E0B2", gravity=false, solid=false})
    ui.notice = makeText("ash_hud_notice", 0, 0, "", "#B9F7FF")
    ui.bossBack = api.spawn("rect", {name="ash_boss_back", x=0, y=-27.5, w=62, h=1.6, color="#34213F", gravity=false, solid=false})
    ui.bossFill = api.spawn("rect", {name="ash_boss_fill", x=-31, y=-27.5, w=0, h=1.2, color="#FF5478", gravity=false, solid=false})
    ui.bossBack.color = "#00000000"
    ui.bossFill.color = "#00000000"
end

local function removeHud()
    local names = {
        "ash_hud_header", "ash_hud_hp", "ash_hud_ability", "ash_hp_back", "ash_hp_fill",
        "ash_hud_notice", "ash_boss_back", "ash_boss_fill"
    }
    for i = 1, #names do removeNamed(names[i]) end
    ui = {}
end

local function updateHud(force)
    if ui.header == nil then return end
    if not force then
        hudTimer = hudTimer - 0.05
        if hudTimer > 0 then return end
    end
    hudTimer = 0.08
    ui.header.text = "WAVE " .. wave .. "/5   SCORE " .. string.format("%06d", score) .. "   LV " .. level
    ui.hpText.text = "HULL " .. math.ceil(hp) .. "/" .. maxHp
    local pulse = pulseCooldown <= 0 and "NOVA READY" or ("NOVA " .. math.ceil(pulseCooldown))
    local dash = dashCooldown <= 0 and "DASH READY" or ("DASH " .. math.ceil(dashCooldown))
    ui.abilityText.text = dash .. "  " .. pulse
    local fill = 42 * clamp(hp / maxHp, 0, 1)
    ui.hpFill.w = math.max(0.1, fill)
    ui.hpFill.x = -49 + fill * 0.5
    if boss ~= nil and not boss.o.dead then
        ui.bossBack.color = "#48243A"
        ui.bossFill.color = "#FF5478"
        local bossFill = 62 * clamp(boss.hp / boss.maxHp, 0, 1)
        ui.bossFill.w = math.max(0.1, bossFill)
        ui.bossFill.x = -31 + bossFill * 0.5
    else
        ui.bossBack.color = "#00000000"
        ui.bossFill.color = "#00000000"
    end
end

local function spawnEffect(x, y, color, size)
    local o = api.spawn("circle", {
        name="rift_flash", x=x, y=y, w=size or 12, h=size or 12,
        color=color or "#8866EFFF", gravity=false, solid=false
    })
    effects[#effects + 1] = {o=o, life=0.18}
end

local function spawnDrop(x, y, kind, amount)
    local sprite = kind == "heal" and "medkit.png" or "shard.png"
    local color = kind == "heal" and "#FFFFFF" or "#FFFFFF"
    local o = api.spawn("sprite", {
        name="rift_drop", sprite=sprite, x=x, y=y, w=5, h=5,
        color=color, gravity=false, solid=false
    })
    drops[#drops + 1] = {o=o, kind=kind, amount=amount or 5, life=13, phase=api.rand(0, 6.28)}
end

local function spawnPlayerBullet(dx, dy)
    local o = api.spawn("sprite", {
        name="pilot_shot", sprite="bolt.png",
        x=player.x + dx * 4.2, y=player.y + dy * 4.2,
        w=3.2, h=3.2, color="#FFFFFF", gravity=false, solid=false
    })
    bullets[#bullets + 1] = {o=o, vx=dx * shotSpeed, vy=dy * shotSpeed, life=1.35, damage=damage, radius=1.7}
end

local function spawnEnemyShot(x, y, dx, dy, speed, hurt)
    local o = api.spawn("sprite", {
        name="rift_bolt", sprite="bolt.png", x=x, y=y,
        w=3.3, h=3.3, color="#FF668E", gravity=false, solid=false
    })
    enemyShots[#enemyShots + 1] = {o=o, vx=dx * speed, vy=dy * speed, life=3.1, damage=hurt or 9, radius=1.8}
end

local function spawnEnemy(kind)
    local x, y
    local edge = math.floor(api.rand(1, 5))
    if edge == 1 then x = -ARENA_X + 2; y = api.rand(-ARENA_Y + 3, ARENA_Y - 3)
    elseif edge == 2 then x = ARENA_X - 2; y = api.rand(-ARENA_Y + 3, ARENA_Y - 3)
    elseif edge == 3 then x = api.rand(-ARENA_X + 3, ARENA_X - 3); y = -ARENA_Y + 2
    else x = api.rand(-ARENA_X + 3, ARENA_X - 3); y = ARENA_Y - 2 end

    local name, sprite, size, ehp, speed, touch, points, radius
    local scale = 1 + wave * 0.13
    if kind == "runner" then
        name, sprite, size, ehp, speed, touch, points, radius = "Skitter", "runner.png", 7, 18 * scale, 19 + wave * 0.8, 8, 130, 3.2
    elseif kind == "brute" then
        name, sprite, size, ehp, speed, touch, points, radius = "Brute", "brute.png", 12, 88 * scale, 7.5 + wave * 0.35, 20, 300, 5.6
    else
        name, sprite, size, ehp, speed, touch, points, radius = "Wraith", "wraith.png", 8, 31 * scale, 11 + wave * 0.45, 11, 180, 3.7
    end
    local o = api.spawn("sprite", {
        name=name, sprite=sprite, x=x, y=y, w=size, h=size,
        color="#FFFFFF", gravity=false, solid=false
    })
    enemies[#enemies + 1] = {
        o=o, kind=kind, hp=ehp, maxHp=ehp, speed=speed, touch=touch,
        score=points, radius=radius, shotTimer=api.rand(1.3, 2.2), phase=api.rand(0, 6.28)
    }
end

local function spawnBoss()
    local o = api.spawn("sprite", {
        name="RIFT_TYRANT", sprite="tyrant.png", x=0, y=-18,
        w=22, h=22, color="#FFFFFF", gravity=false, solid=false
    })
    boss = {o=o, kind="boss", hp=880, maxHp=880, speed=7.2, touch=24,
        score=5000, radius=10.5, shotTimer=1.5, phase=0}
    enemies[#enemies + 1] = boss
    spawnEffect(0, -18, "#AAFF4D91", 34)
    notice("THE RIFT TYRANT HAS AWAKENED", "#FF7896")
    api.play("boss.wav")
    api.log("ASHFALL: final guardian entered the arena")
end

local function beginWave()
    state = "playing"
    noticeTimer = 0
    if wave == 5 then
        bossSpawned = true
        spawnLeft = 0
        spawnBoss()
    else
        spawnLeft = 6 + wave * 2
        spawnTimer = 0.7
        notice("SECTOR " .. wave .. "  -  HOLD THE BEACON", "#8EEBFF")
    end
end

local function startHudAndGame()
    removeHud()
    destroyList(enemies)
    destroyList(bullets)
    destroyList(enemyShots)
    destroyList(drops)
    destroyList(effects)
    boss = nil
    score, kills, level, xp = 0, 0, 1, 0
    xpNeed = 14
    hp, maxHp = 100, 100
    damage, fireDelay, moveSpeed = 18, 0.23, 29
    fireTimer, dashTimer, dashCooldown, invulnerable, pulseCooldown = 0, 0, 0, 0, 0
    wave = 1
    bossSpawned = false
    intermissionTimer = 0
    lastDirX, lastDirY = 1, 0
    player = api.find("pilot")
    if player ~= nil then
        player.x, player.y = 0, 2
        player.color = "#FFFFFF"
    end
    for _, name in ipairs({"menu_title", "menu_subtitle", "menu_story", "menu_controls1", "menu_controls2", "menu_controls3", "menu_start", "menu_best"}) do
        removeNamed(name)
    end
    createHud()
    state = "playing"
    beginWave()
    api.log("ASHFALL run started")
end

local function startMenu()
    state = "menu"
    destroyList(enemies)
    destroyList(bullets)
    destroyList(enemyShots)
    destroyList(drops)
    destroyList(effects)
    removeUpgradeUi()
    boss = nil
    player = api.find("pilot")
    if player ~= nil then player.x, player.y = 0, 2; player.color = "#FFFFFF" end
    bestScore = tonumber(api.load("ashfall_best")) or 0
    removeHud()
    makeText("menu_title", 0, -22, "ASHFALL", "#82EFFF")
    makeText("menu_subtitle", 0, -15, "THE LAST BEACON", "#F5C96A")
    makeText("menu_story", 0, 7, "THE RIFT IS OPEN. HOLD THE LINE.", "#B8D7EB")
    makeText("menu_controls1", 0, 17, "WASD / ARROWS  -  MOVE", "#9CC6DA")
    makeText("menu_controls2", 0, 22, "MOUSE AIM  -  HOLD J OR F TO FIRE", "#9CC6DA")
    makeText("menu_controls3", 0, 27, "SHIFT / SPACE DASH  -  Q NOVA  -  P PAUSE", "#9CC6DA")
    makeText("menu_start", 0, 36, "PRESS ENTER OR CLICK TO DEPLOY", "#7DFFCD")
    makeText("menu_best", 0, 41, "BEST RUN  " .. bestScore, "#F5C96A")
end

local function spawnUpgradeUi()
    upgradeUi = {}
    upgradeUi.head = makeText("upgrade_head", 0, -12, "BEACON EVOLVED  -  CHOOSE ONE", "#7DFFCD")
    upgradeUi.one = makeText("upgrade_one", 0, 0, "1  DAMAGE CORE", "#F5C96A")
    upgradeUi.two = makeText("upgrade_two", 0, 7, "2  RAPID FIRE", "#82EFFF")
    upgradeUi.three = makeText("upgrade_three", 0, 14, "3  REINFORCED HULL", "#9CFFB8")
    ui.notice.text = "LEVEL " .. level .. "  -  PICK AN UPGRADE"
end

removeUpgradeUi = function()
    removeNamed("upgrade_head")
    removeNamed("upgrade_one")
    removeNamed("upgrade_two")
    removeNamed("upgrade_three")
    upgradeUi = {}
end

local function beginUpgrade()
    state = "upgrade"
    local pool = {"damage", "rate", "hull", "speed"}
    upgradeChoices = {}
    for i = 1, 3 do
        local index = math.floor(api.rand(1, #pool + 0.999))
        upgradeChoices[i] = table.remove(pool, index)
    end
    local labels = {
        damage = "DAMAGE CORE  +25% SHOT POWER",
        rate = "RAPID FIRE  +20% FIRE RATE",
        hull = "REINFORCED HULL  +30 MAX HP",
        speed = "PHASE DRIVE  +12% MOVE SPEED"
    }
    spawnUpgradeUi()
    upgradeUi.one.text = "1  " .. labels[upgradeChoices[1]]
    upgradeUi.two.text = "2  " .. labels[upgradeChoices[2]]
    upgradeUi.three.text = "3  " .. labels[upgradeChoices[3]]
end

local function chooseUpgrade(index)
    local choice = upgradeChoices[index]
    if choice == nil then return end
    if choice == "damage" then damage = damage * 1.25
    elseif choice == "rate" then fireDelay = math.max(0.09, fireDelay * 0.80)
    elseif choice == "hull" then maxHp = maxHp + 30; hp = math.min(maxHp, hp + 45)
    elseif choice == "speed" then moveSpeed = moveSpeed * 1.12 end
    removeUpgradeUi()
    notice("UPGRADE INSTALLED", "#7DFFCD")
    state = "playing"
    updateHud(true)
    if xp >= xpNeed then
        xp = xp - xpNeed
        level = level + 1
        xpNeed = 14 + level * 5
        beginUpgrade()
    end
end

local function grantXp(amount)
    xp = xp + amount
    if xp >= xpNeed then
        xp = xp - xpNeed
        level = level + 1
        xpNeed = 14 + level * 5
        beginUpgrade()
    end
end

local function killEnemy(index)
    local e = table.remove(enemies, index)
    if e == nil then return end
    if not e.o.dead then e.o.destroy() end
    score = score + e.score
    kills = kills + 1
    spawnEffect(e.o.x, e.o.y, e.kind == "boss" and "#AAFF4D91" or "#8873EFFF", e.kind == "boss" and 34 or 12)
    if e.kind == "boss" then
        boss = nil
        score = score + 5000
        spawnDrop(e.o.x, e.o.y, "xp", 35)
        notice("THE TYRANT IS BROKEN", "#7DFFCD")
    else
        if api.rand(0, 1) < 0.82 then spawnDrop(e.o.x, e.o.y, "xp", e.kind == "brute" and 8 or 5) end
        if api.rand(0, 1) < 0.10 then spawnDrop(e.o.x + 2, e.o.y, "heal", 24) end
    end
end

local function damagePlayer(amount, fromX, fromY)
    if invulnerable > 0 or state ~= "playing" then return end
    hp = hp - amount
    invulnerable = 0.78
    if player ~= nil then
        local dx = player.x - fromX
        local dy = player.y - fromY
        local d = math.sqrt(dx * dx + dy * dy)
        if d > 0.01 then
            player.x = clamp(player.x + dx / d * 4.5, -ARENA_X + 4, ARENA_X - 4)
            player.y = clamp(player.y + dy / d * 4.5, -ARENA_Y + 4, ARENA_Y - 4)
        end
        player.color = "#FF9EAD"
    end
    spawnEffect(player.x, player.y, "#AAFF5577", 14)
    api.play("damage.wav")
    if hp <= 0 then
        hp = 0
        state = "defeat"
        if score > bestScore then bestScore = score; api.save("ashfall_best", tostring(bestScore)) end
        notice("BEACON LOST  -  PRESS ENTER TO RETRY", "#FF7896")
        api.log("ASHFALL run ended: defeat, score " .. score)
    else
        notice("HULL DAMAGED", "#FF9EAD")
    end
    updateHud(true)
end

local function fireAtTarget()
    local targetX, targetY
    if api.click() then
        targetX, targetY = api.mousex(), api.mousey()
    else
        local stickX = api.axis("look_x")
        local stickY = -api.axis("look_y")
        if math.abs(stickX) + math.abs(stickY) > 0.22 then
            targetX, targetY = player.x + stickX * 30, player.y + stickY * 30
        end
        local bestDist = 100000
        local target = nil
        for i = 1, #enemies do
            local e = enemies[i]
            local d = distance(player.x, player.y, e.o.x, e.o.y)
            if d < bestDist then bestDist = d; target = e end
        end
        if targetX == nil and target ~= nil then targetX, targetY = target.o.x, target.o.y
        elseif targetX == nil then targetX, targetY = player.x + lastDirX * 20, player.y + lastDirY * 20 end
    end
    local dx = targetX - player.x
    local dy = targetY - player.y
    local d = math.sqrt(dx * dx + dy * dy)
    if d < 0.001 then dx, dy, d = lastDirX, lastDirY, 1 end
    dx, dy = dx / d, dy / d
    spawnPlayerBullet(dx, dy)
    api.play("laser.wav")
end

local function useNova()
    if pulseCooldown > 0 then return end
    pulseCooldown = 9.5
    spawnEffect(player.x, player.y, "#AA4BCEFF", 38)
    api.play("nova.wav")
    for i = #enemies, 1, -1 do
        local e = enemies[i]
        if distance(player.x, player.y, e.o.x, e.o.y) < 24 then
            e.hp = e.hp - (damage * 3.1)
            e.o.color = "#FFFFFF"
            if e.hp <= 0 then killEnemy(i) end
        end
    end
    for i = #enemyShots, 1, -1 do
        local b = enemyShots[i]
        if distance(player.x, player.y, b.o.x, b.o.y) < 25 then
            if not b.o.dead then b.o.destroy() end
            table.remove(enemyShots, i)
        end
    end
    notice("NOVA PULSE", "#A7C5FF")
end

local function updatePlayer(dt)
    local mx = api.axis("move_x")
    local my = api.axis("move_y")
    -- MonoGame thumbstick Y is positive upwards, while LIC world Y grows downwards.
    -- Keyboard/D-pad action bindings are already expressed in screen coordinates.
    if api.action("move_up") then my = -1
    elseif api.action("move_down") then my = 1
    else my = -my end
    local length = math.sqrt(mx * mx + my * my)
    if length > 1 then mx, my = mx / length, my / length end
    if math.abs(mx) + math.abs(my) > 0.05 then
        local norm = math.sqrt(mx * mx + my * my)
        if norm > 0.001 then lastDirX, lastDirY = mx / norm, my / norm end
    end
    if api.action_pressed("dash") or api.action_pressed("jump") then
        if dashCooldown <= 0 then
            dashTimer = 0.19
            dashCooldown = 2.6
            invulnerable = math.max(invulnerable, 0.30)
            spawnEffect(player.x, player.y, "#8863EFFF", 17)
        end
    end
    local multiplier = 1
    if dashTimer > 0 then
        dashTimer = math.max(0, dashTimer - dt)
        multiplier = 3.1
        mx, my = lastDirX, lastDirY
    end
    player.x = clamp(player.x + mx * moveSpeed * multiplier * dt, -ARENA_X + 4, ARENA_X - 4)
    player.y = clamp(player.y + my * moveSpeed * multiplier * dt, -ARENA_Y + 4, ARENA_Y - 4)
    if invulnerable > 0 then
        invulnerable = math.max(0, invulnerable - dt)
        if math.floor(gameClock * 18) % 2 == 0 then player.color = "#FFFFFF" else player.color = "#8EEBFF" end
    else
        player.color = "#FFFFFF"
    end
    dashCooldown = math.max(0, dashCooldown - dt)
    pulseCooldown = math.max(0, pulseCooldown - dt)
    fireTimer = math.max(0, fireTimer - dt)
    if (api.action("fire") or api.key("f") or api.click()) and fireTimer <= 0 then
        fireTimer = fireDelay
        fireAtTarget()
    end
    if api.action_pressed("pulse") then useNova() end
end

local function updateEnemies(dt)
    for i = #enemies, 1, -1 do
        local e = enemies[i]
        if e.o.dead then
            table.remove(enemies, i)
        else
            local dx = player.x - e.o.x
            local dy = player.y - e.o.y
            local d = math.sqrt(dx * dx + dy * dy)
            if d > 0.01 then
                local speed = e.speed
                if e.kind == "boss" then
                    speed = e.speed * (d > 30 and 1.0 or 0.55)
                end
                e.o.x = e.o.x + dx / d * speed * dt
                e.o.y = e.o.y + dy / d * speed * dt
            end
            if d < e.radius + PLAYER_RADIUS then damagePlayer(e.touch, e.o.x, e.o.y) end
            if e.kind == "boss" then
                e.shotTimer = e.shotTimer - dt
                if e.shotTimer <= 0 then
                    e.shotTimer = 1.65
                    local base = gameClock * 0.65
                    for n = 0, 7 do
                        local a = base + n * 0.785398
                        spawnEnemyShot(e.o.x, e.o.y, math.cos(a), math.sin(a), 17, 11)
                    end
                end
            end
        end
    end
end

local function updatePlayerBullets(dt)
    for i = #bullets, 1, -1 do
        local b = bullets[i]
        b.life = b.life - dt
        b.o.x = b.o.x + b.vx * dt
        b.o.y = b.o.y + b.vy * dt
        local hit = false
        for j = #enemies, 1, -1 do
            local e = enemies[j]
            if distance(b.o.x, b.o.y, e.o.x, e.o.y) < e.radius + b.radius then
                e.hp = e.hp - b.damage
                e.o.color = "#FFFFFF"
                if not b.o.dead then b.o.destroy() end
                table.remove(bullets, i)
                if e.hp <= 0 then killEnemy(j) end
                hit = true
                break
            end
        end
        if not hit and (b.life <= 0 or math.abs(b.o.x) > 77 or math.abs(b.o.y) > 37) then
            if not b.o.dead then b.o.destroy() end
            table.remove(bullets, i)
        end
    end
end

local function updateEnemyShots(dt)
    for i = #enemyShots, 1, -1 do
        local b = enemyShots[i]
        b.life = b.life - dt
        b.o.x = b.o.x + b.vx * dt
        b.o.y = b.o.y + b.vy * dt
        if distance(player.x, player.y, b.o.x, b.o.y) < PLAYER_RADIUS + b.radius then
            if not b.o.dead then b.o.destroy() end
            table.remove(enemyShots, i)
            damagePlayer(b.damage, b.o.x, b.o.y)
        elseif b.life <= 0 or math.abs(b.o.x) > 77 or math.abs(b.o.y) > 37 then
            if not b.o.dead then b.o.destroy() end
            table.remove(enemyShots, i)
        end
    end
end

local function updateDrops(dt)
    for i = #drops, 1, -1 do
        if state == "upgrade" then return end
        local d = drops[i]
        d.life = d.life - dt
        d.phase = d.phase + dt * 3
        local dx = player.x - d.o.x
        local dy = player.y - d.o.y
        local dist = math.sqrt(dx * dx + dy * dy)
        if dist < 18 and dist > 0.1 then
            d.o.x = d.o.x + dx / dist * 16 * dt
            d.o.y = d.o.y + dy / dist * 16 * dt
        end
        if dist < 5.5 then
            if d.kind == "heal" then
                hp = math.min(maxHp, hp + d.amount)
                notice("HULL REPAIRED  +" .. d.amount, "#80FFB5")
                api.play("pickup.wav")
            else
                grantXp(d.amount)
                score = score + 15
                api.play("pickup.wav")
            end
            if not d.o.dead then d.o.destroy() end
            table.remove(drops, i)
        elseif d.life <= 0 then
            if not d.o.dead then d.o.destroy() end
            table.remove(drops, i)
        end
    end
end

local function updateEffects(dt)
    for i = #effects, 1, -1 do
        local fx = effects[i]
        fx.life = fx.life - dt
        if fx.life <= 0 then
            if not fx.o.dead then fx.o.destroy() end
            table.remove(effects, i)
        end
    end
end

local function updateWave(dt)
    if wave < 5 and spawnLeft > 0 then
        spawnTimer = spawnTimer - dt
        if spawnTimer <= 0 then
            local r = api.rand(0, 1)
            local kind = "wraith"
            if wave >= 3 and r < 0.18 then kind = "brute"
            elseif wave >= 2 and r < 0.52 then kind = "runner" end
            spawnEnemy(kind)
            spawnLeft = spawnLeft - 1
            spawnTimer = math.max(0.34, 0.92 - wave * 0.09)
        end
    end
    if wave < 5 and spawnLeft <= 0 and #enemies == 0 then
        state = "intermission"
        intermissionTimer = 3.2
        hp = math.min(maxHp, hp + 12)
        notice("SECTOR CLEARED  -  HULL +12", "#7DFFCD")
        updateHud(true)
    elseif wave == 5 and bossSpawned and #enemies == 0 then
        state = "victory"
        if score > bestScore then bestScore = score; api.save("ashfall_best", tostring(bestScore)) end
        notice("THE LAST BEACON STANDS", "#7DFFCD")
        api.log("ASHFALL victory: score " .. score .. ", kills " .. kills)
    end
end

local function updatePlaying(dt)
    if player == nil or player.dead then
        state = "defeat"
        notice("PILOT LOST  -  PRESS ENTER", "#FF7896")
        return
    end
    updatePlayer(dt)
    updateEnemies(dt)
    if state ~= "playing" then updateHud(true); return end
    updatePlayerBullets(dt)
    updateEnemyShots(dt)
    if state ~= "playing" then updateHud(true); return end
    updateDrops(dt)
    if state == "upgrade" then updateHud(true); return end
    updateEffects(dt)
    updateWave(dt)
    if noticeTimer > 0 then
        noticeTimer = math.max(0, noticeTimer - dt)
        if noticeTimer == 0 and state == "playing" and ui.notice ~= nil then ui.notice.text = "" end
    end
    updateHud(false)
end

local function restartOrMenu()
    if state == "victory" or state == "defeat" then
        startMenu()
        return
    end
end

function start()
    bindControls()
    startMenu()
    api.log("ASHFALL loaded | WASD move, mouse aim, J/F fire, Shift dash, Q nova")
end

function update(dt)
    if not (dt == dt) or dt <= 0 then return end
    dt = math.min(dt, 0.05)
    gameClock = gameClock + dt

    if state == "menu" then
        if api.key("enter") or api.key("space") or api.click() then
            startHudAndGame()
        end
        return
    end

    if state == "upgrade" then
        if api.key("D1") then chooseUpgrade(1)
        elseif api.key("D2") then chooseUpgrade(2)
        elseif api.key("D3") then chooseUpgrade(3) end
        return
    end

    if state == "paused" then
        if api.action_pressed("pause") or api.key("enter") then
            state = "playing"
            notice("BACK IN THE RIFT", "#8EEBFF")
        end
        return
    end

    if state == "intermission" then
        updateEffects(dt)
        intermissionTimer = intermissionTimer - dt
        if intermissionTimer <= 0 then
            wave = wave + 1
            beginWave()
            updateHud(true)
        end
        return
    end

    if state == "victory" or state == "defeat" then
        if api.key("enter") or api.key("space") or api.click() then restartOrMenu() end
        return
    end

    if state == "playing" then
        if api.action_pressed("pause") then
            state = "paused"
            notice("PAUSED  -  PRESS P OR ENTER", "#B8D7EB")
            return
        end
        updatePlaying(dt)
    end
end
