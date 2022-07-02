# InWin 309 MAX
![](https://www.in-win.com/uploads/Product/gaming-chassis/309_gaming_edition/309_ge_lighten_03.png)

The [InWin 309 PC tower](https://www.in-win.com/en/gaming-chassis/309) features a 8x18 ARGB led front panel.  
This console app tries to push the limits of the panel by making it programmable. For example, you can draw GIFs to have a de-facto unlimited amount of effects.

Currently this program is highly experimental and not meant for end-users.

## Features
- Modern code: Thread safe class library for interacting with the panel in a non-blocking way.
- Drawing images and GIFs using [ImageSharp](https://docs.sixlabors.com/articles/imagesharp/index.html).
- Supports audio visualizer mode.

## Limitations
- Visual glitches occur when drawing images too fast. This is very noticable on drawing GIFs and a hardware / firmware limitation that cannot fixed from software side.
- Images / GIFs must be 8x18 or smaller (they are not automatically scaled yet).
- Audio visualizer has visual glitches.
- You may temporarily break USB connection to your panel when drawing images too fast (> 0.3 FPS). Restarting or sleeping-waking up your PC will fix this.

## License
All source code is available under the MIT license.

## Disclaimer
I take no responsiblity for any potential damages caused to your panel.  
Sending invalid commands may permanently brick your panel or your GLOW2 software.  
USE AT YOUR OWN RISK.