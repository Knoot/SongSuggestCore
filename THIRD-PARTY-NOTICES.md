# BeatLeader PPv4

The no-modifier PPv4 mathematics in `SongSuggestCore/Data/Curve/BeatLeaderCurve.cs`
is adapted from BeatLeader RatingAPI (revision
`e9d49b085aec25f0e91959de2219daec912260e2`, `Pp/PpFormula.cs`,
`Pp/PpCalculator.cs`, `Controllers/Curve.cs`) and the equivalent website
implementation (`6950ac73ef912ad5c0b08071f723238666ec572d`,
`src/utils/beatleader/rebalanced-pp.js`). Adaptations target .NET Standard 2.0,
use double precision, and omit unsupported leaderboard contexts and modifiers.

https://github.com/BeatLeader/RatingAPI

MIT License

Copyright (c) 2023 BeatLeader

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
