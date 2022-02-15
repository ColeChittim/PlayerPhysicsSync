# PlayerPhysicsSync Example Project (Unity / PUN 2)
This project is designed as a general example/template to perform player physics synchronization over the network.
This code was originally based on the code posted by JOE BEST-ROTHERAY on his blog, https://www.codersblock.org/blog/client-side-prediction-in-unity-2018

It is based on the code I used in my game Jelly Brawl: https://store.steampowered.com/app/1278350/Jelly_Brawl/

And is what I am refrencing in the Tutorial video I did on the subject: https://youtu.be/ZN0tKCPZSUg

In the example I use the PUN 2 api for sending data. However, this can easily be applied to any standard networking library.
I have commented throughout the code marking where you need to apply your game's logic as well as possible places for modifications and improvements.
Hopefully this example can benefit someone.

Twitter: https://twitter.com/ColeChittim

# 2022 Update:
I recently completely did a rework of my game Jelly Brawl's networking using Photon's new framework fusion and it makes the problem of authoritative physics as simple as adding a component to networked physics objects. If your struggling with this in your project or considering tackling this in an upcoming project I recommend checking it out.
https://doc.photonengine.com/en-us/fusion/current/getting-started/fusion-intro
