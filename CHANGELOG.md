# Changelog

## 1.0.1

- Confirmed compatible with Valheim 1.0.30. All stamina and combat patch targets still match.
- Improved: stamina patches now swap the game's `UseStamina` call for a small helper instead of inserting branch logic around it, which is safer across game updates. If a call can't be found, that method stays vanilla and a `Compatibility:` warning is logged.

## 1.0.0

- Initial version.
