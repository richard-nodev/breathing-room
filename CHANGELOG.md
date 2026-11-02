# Changelog

All notable changes to this project are documented here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [Semantic Versioning](https://semver.org/).

## [1.0.1] - 2026-11-02

### Fixed

- While the PC is offline, a failed update check is noted in the log once per run instead of every hour.

## [1.0.0] - 2026-11-02

The first public release.

### Mochi

- A hand-drawn desk cat that blinks, follows your mouse, and leans into your hand when you pet it, purring louder the longer you do. It naps when you're away and stays still while a game is in front. You can drag it anywhere and rename it.
- **One key to log** (`Ctrl+Alt+S`, changeable). It works inside games, with no alt-tab. Mochi answers with plain facts: the time since the last one, today's count, and your longest stretch. A "changed my mind" link undoes the log.
- **Little questions**, now and then: "Did you decide on this one, or did your hand?", "How's this one tasting?", "What's going on right now?". Answer with one click, or ignore them.
- **"Did one slip past me?"** Mochi learns your usual gap and asks once if a gap runs unusually long, or when you come back from a break.
- **Weekly recap** and a **one-week milestone**, each shown once.
- **Breathe with me**: a one-minute breathing exercise with a soft purr, then "Still want one, or did it pass?".
- **Stretch breaks**: after 45 minutes, an hour or 90 minutes at the PC (your choice, or never), Mochi suggests standing up for a bit. It never interrupts a fullscreen game.
- Optional spoken facts (Windows voice) for exclusive-fullscreen games, which hide every window. They're off by default; a real meow confirms the log either way.

### On screen

- **Afterglow**: a warm glow on the screen edge that fades over about an hour after each cigarette. It has no numbers and no "allowed now" moment.
- An optional **corner badge**: `12m · #7 today`.

### How it's going

- **At a glance:** today vs. yesterday at the same time, a 14-day chart, and your average and longest gaps.
- **What your answers show,** written as plain sentences:
  - how often your hand lit one before you decided
  - which app or moment comes up most
  - your busiest hours
  - how many times you changed your mind or let an urge pass
- **Your data:** it updates live, and you can remove entries logged by mistake.

### Play

- **Four rooms** (living room, garden, rooftop at night, little café). They follow the real time of day: morning light, sunset, and lamps and fireflies at night.
- **Nine games**, with Mochi in every one:
  - **Rooftop run**: one night across town, from sunset to sunrise, in eight parts. Each brings something new: washing lines, bouncy market awnings, a calm balloon ride, a night train, lantern lines to slide along, the harbor and gardens at dawn. Also fish streaks up to ×5, postcards to collect, and a flag where your best run ended. It's made to absorb rather than stress: steady speed, a slip eases things off, and a heart comes back in every part of town.
  - **Café rush**: guest cats order fish, milk, cake and cookies, and Mochi serves them. Clicks queue up, Mochi carries two treats at a time, and every day brings more guests with less patience.
  - Seven quick ones: Fetch, Laser, Treats, Bubbles, Toy mouse, Hide & seek and Night flight.
- **New round** starts the game over; the room button only changes the room.
- Rooms with soft light and shadows, and the play window pauses while it isn't in front.

### Mochi's look and friendship

- **Looks:** six coats and five accessories, unlocked by a friendship level that grows from time together (petting, playing, breathing, answering). It never depends on how much you smoke.
- **Ride the wave:** a three-minute urge-surfing guide, next to the one-minute *Breathe with me*.
- **When you smoke:** a day-by-hour heatmap in *How it's going*.

### Sound

- **Real cat recordings** (CC0, by Joseph Sardin, BigSoundBank): meows, sleepy mews and three purrs. There are no synthesized sounds.
- **Mochi's voice:** listen to every clip and tick when Mochi may use it. Your own `.wav` files in the sounds folder take priority.

### Privacy and data

- **Everything stays on your PC.** No account, no tracking.
- **Export to CSV**, **delete my history**, and a switch to stop remembering which app was open.

### Updates

- **Update from inside the app.** About once a day the app asks GitHub whether a new version is out (nothing about you is sent; switch it off in Settings). Mochi mentions a new version once, and **Update now** in the tray or *About* downloads it, checks its SHA-256 checksum, swaps it in and restarts, keeping your data.

### Project

- A single-file exe, built with the C# compiler that ships with Windows. Nothing to install.
- 90 logic tests run on every build. There is also a UI smoke test that clicks through every screen, a GitHub Actions build, and a release workflow.
- **Light on your PC:** like a real cat, Mochi mostly sits still (a blink, a tail flick now and then), keeps still while a fullscreen game is in front, and stops animating entirely while hidden. The games cap themselves at about 60 frames a second, pause in the background, keep real time on slow PCs, and pick graphics-card or software drawing, whichever this PC does faster.
- **Safe with your data:** saves are atomic with a backup copy. An unreadable data file is kept aside instead of being overwritten, and times stay correct across daylight-saving changes.

[1.0.1]: https://github.com/richard-nodev/breathing-room/releases/tag/v1.0.1
[1.0.0]: https://github.com/richard-nodev/breathing-room/releases/tag/v1.0.0
