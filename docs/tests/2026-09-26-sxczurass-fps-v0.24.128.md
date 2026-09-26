# sxczurass - FPS follow-up, v0.24.128, 2026-09-26

Posted in #general as a reply to his Frame test log (message
`1553515759936999506`):
https://discord.com/channels/1553092607134011542/1553092608509874318/1553517261149442159
(not in `qa/*.txt`).

What each item checks:
1. GPU vs render thread: his Frame test showed a rendering-bound main
   thread (+1 ms of work did not lengthen the frame). A lower resolution
   raising fps = the GPU; unchanged = the render thread (draw calls).
2. The two Experimental camera switches (`SunShadowsEveryOtherFrame`,
   `GrassBendingOffInCaves`) on his machine: the Frame line with them on
   vs off, and whether he sees any difference (the author saw none for
   the sun shadows).
3. The log with both.

```
1) Play 1 minute at a lower resolution (for example 1280x720), nothing else changed, then put it back. Tells us if the graphics card or the processor is the limit.
2) Debug views -> Performance patches, Experimental section: turn on "Sun shadows: redraw every second frame" and "Caves: no grass bending while inside". Play 1-2 minutes on the surface and a bit in a cave. Say if anything looks different.
3) Send the log (BepInEx/LogOutput.log) like before.
```
