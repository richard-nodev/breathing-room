# Design

Why Breathing Room works the way it does. Read this before changing what the cat says or does.

## The problem it solves

Most "extra" cigarettes aren't decisions. They're **autopilot**:

- **Absorption.** Deep in a game or in work, you light one five minutes after the last without noticing. Three in half an hour happens this way.
- **Empty time.** Waiting on a build, a loading screen or an AI assistant is boring, and a cigarette fills the gap.

The people this is for often want to cut down, not quit. Many react badly to being told what to do.

## What doesn't work (and why the app avoids it)

- **Rules and countdowns.** For many smokers a rule triggers *reactance*: the urge to do the opposite. A countdown also turns the next cigarette into an appointment, and the hour gets spent waiting for it. So: no targets, no timers, no "too early".
- **Lectures, warnings, motivational quotes.** They feel like rehab, make people feel worse, and get tuned out. So: none.
- **Anything that becomes wallpaper.** A static number on screen is ignored within days.

## What does work (and what the app does instead)

1. **Noticing, right at the moment.** Self-monitoring on its own reliably reduces smoking. One key logs a cigarette from inside a game, and the cat answers with a plain fact: *"Huh, 6 min since the last one. #7 today."* That one moment of noticing is what breaks autopilot. "Changed my mind" is always one click away.
2. **Curiosity instead of willpower.** Every now and then the cat asks something light: *"Did you decide on this one, or did your hand?"*, *"How's this one tasting?"*. Paying attention to what a cigarette is actually like slowly takes the shine off it. This is the idea behind Judson Brewer's curiosity-based approach. The answers come back as the user's own discoveries in "How it's going", never as advice.
3. **Autonomy.** Information and choices, never instructions. The app can't lock, block or scold. It has one optional, user-started exercise (breathe for a minute), and afterwards it asks *"Still want one, or did it pass?"*. Either answer is fine.
4. **Staying alive on screen.** A living cat that blinks, follows the mouse, naps and purrs doesn't turn into wallpaper the way a widget does. The afterglow fades instead of counting down. Lines vary. Notes like the weekly recap appear once and then go.
5. **Honest data over perfect data.** Logging an early cigarette costs nothing. Forgotten ones can be added later, and the cat asks gently ("did one slip past me?") when a gap runs much longer than the user's own usual one, or after a break.

## Play

Empty minutes are when many cigarettes happen, so the play window gives the hands and the mind something else to do. A few rules shape every game:

- **Absorbing, not stressful.** Stress raises the urge to smoke, so nothing punishes hard. In the rooftop run, variety comes from new places and new things to do rather than from going faster. The speed stays moderate, a slip slows things down for a moment instead of piling on, and every new part of town gives a heart back. With the last heart or two left, the roofs ahead quietly get roomier. Collisions are judged a little inside the cat's outline, so a jump that looks like it made it did make it.
- **Long enough to outlast an urge.** An urge usually rises and falls within a few minutes. A lap of the rooftop run takes about six minutes. The balloon ride in the middle paces its waves of fish like slow breathing, about four seconds up and six down, without ever saying so.
- **Fair, and checked.** A test bot that replays the game's own physics plays every run, so no generated stretch of roofs is impossible. Bots with a person's timing error play it too, so it stays forgiving for casual players.
- **Rewards for time together, never for smoking less.** Friendship, looks, best scores and postcards come from playing, petting and answering, and never depend on how much or how little someone smokes.

## Tone

A friend at the next desk who happens to be a cat:

- **Short and plain**, with a little humor (`*stretches*`), and mostly silent.
- **Facts are fine.** "Heads up: 24 a day the last 3 days, 19 the week before."
- **Never** "you should", "you can do it", "too early", health warnings, or medical claims.

## Privacy

Everything lives in one local JSON file, `%APPDATA%\BreathingRoom\data.json`. There's no account and no telemetry. The one network request is the update check: about once a day the app reads the project's latest release from GitHub, sends nothing about the user, and can be switched off in Settings. An update is only installed when the user clicks *Update now*, and only if it matches its published SHA-256 checksum.

The name of the app in front when a cigarette is logged is stored to show patterns. It can be switched off. Users can export everything to CSV or delete their history.

## Sound

Only real recordings: CC0 cat recordings by Joseph Sardin (BigSoundBank). Some meows sound like a duckling to some ears, so the user picks which clips Mochi uses for each moment. Unticked clips never play. There are no synthesized sounds.

## Technical choices

- **Builds with no installs.** C# 5 + WPF on .NET Framework 4.8, built with `csc.exe` from `C:\Windows\Microsoft.NET`. That's why the code avoids newer C# features.
- **The UI is written in code**, with XAML loaded at runtime only for a few control styles.
- **One exe.** The sounds and icon are embedded as resources.
- **Overlays never get in the way of games.** They are topmost, hidden from Alt-Tab and never take focus. Everywhere except the cat and its bubble is click-through. Animations run at low frame rates, so CPU use is near zero.
- **Light on weak PCs.** The play window pauses whenever it isn't in front and never runs faster than about 60 frames a second. Games step in about 1/60 s slices, several per frame when frames are few, so a slow PC shows fewer frames but never slow motion. The games use no blur or shadow effects.
- **Graphics card or software, whichever is faster.** On a graphics card, rooms are drawn once into cached bitmaps and the rooftop's buildings slide as whole pictures. But some PCs, virtual machines and remote desktops manage only a few frames a second on the card. So the first play window of each session times two seconds there; if that's choppy, it times software drawing and keeps it when it's smoother or about as smooth for less work. In software, caching costs more than it saves, so it goes off too. (Measured on a VMware VM: 5–17 fps on the card at 40–120 ms of CPU per frame, against 14–60 fps in software at 1.5–9 ms.)
- **Updates swap the exe in place.** A running exe can be renamed but not overwritten, so the updater renames it to `.old`, copies the new one in, and starts it; the new copy waits for the old one to quit and then deletes the `.old` file.
- **Exclusive-fullscreen games** hide every window. The app detects them (`SHQueryUserNotificationState`) and speaks the fact instead.
- **Days start at 05:00**, so late-night sessions count toward the evening they began. A gap over 6 hours, or one that crosses that boundary, is "overnight" and left out of averages and records.
- **Saves are atomic**: write to a temp file, then replace, keeping a `.bak` copy.
- **Test mode:** set `BR_DATA_DIR` and the app uses that folder, never touches the startup list, and can be driven through `command.txt`. That's how the UI smoke test and the README pictures work.
