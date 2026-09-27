-- Window rules for the price overlay on Hyprland.
--
-- Without them the compositor treats the overlay like any other window: it blurs whatever is behind it
-- (which is the game, and is what makes the stash look washed out), draws a border and a shadow around it,
-- animates it, fades it because it is never focused, and lets it take focus from the game.
--
-- The app applies these itself over Hyprland's IPC socket when it starts, so this file is only needed if
-- you turned that off ("ApplyCompositorRules": false) or would rather have them in your own config.
--
-- Hyprland 0.56 and later read Lua. Require this from ~/.config/hypr/hyprland.lua, or paste the call into
-- your own config/windowrules.lua.

hl.window_rule({
    match = { class = "Poe2StashPricer.App" },

    float = true,
    center = false,        -- a generic "centre every floating window" rule would move it off the stash
    persistent_size = false,
    pin = true,            -- stays put when the workspace changes
    no_focus = true,       -- never takes the keyboard from the game
    no_follow_mouse = true,

    no_blur = true,        -- the important one: otherwise the game behind it is blurred
    no_shadow = true,
    no_anim = true,
    no_dim = true,
    opacity = 1.0,         -- inactive_opacity would otherwise fade the prices
    border_size = 0,
    rounding = 0,
})
