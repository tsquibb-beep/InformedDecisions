# InformedDecisions

A Slay the Spire 2 mod that shows two things on a card that the game makes easy to miss.

- **Normal cost.** When an effect changes a card's cost (Void Form, Corruption, Bullet Time,
  Enlightenment, Borrowed Time and so on), a smaller copy of the cost gem behind the cost shows
  what the card normally costs. This works for raised and lowered costs, and for the Regent's star costs.
  Cost cuts from upgrading a card are not shown.
- **Replay.** A card that will be played more than once shows how many extra times inside two arching arrows, in a second, dimmer
  small gem by the cost, which can ripple or glow gently while the card is in your hand. A setting
  can also stack card edges behind it (up to two). This counts Replay on
  the card (including from enchantments and Hidden Gem) and extra plays from powers and relics
  such as Echo Form, Burst and Duplication.

Both show on every card that has a cost: in your hand, in the piles, in the deck view and in
rewards. The mod only changes what is drawn on the card, so it is safe in co-op.

## Install

Copy the `InformedDecisions` folder into `Slay the Spire 2/mods/`, or install with Vortex.

## Settings

`InformedDecisions.config.jsonc` in the mod folder turns each part on or off and sets the sizes,
colours and positions. To keep your settings through updates, copy it to
`%APPDATA%\SlayTheSpire2\`; that copy is used first.

With the dev console open (any mod enables it), `idec reload` applies changes to the config
without restarting, `idec effect ripple` (or `glow`, `compare`, `none`) switches the replay gem's
animation, and `idec diag` lists the values for each card in your hand.

## Licence

MIT
