// Raw mouse movement for macOS.
//
// The pointer movement macOS hands to applications has the system's acceleration curve applied:
// move the mouse faster and the same distance on the desk turns you further. This asks the
// GameController framework for the movement the mouse itself reports, before any of that, so a
// given distance on the desk is always the same turn. It needs no extra permission from macOS.
// Trackpads are not covered; the game falls back to ordinary pointer movement for those.
//
// Build: Tools/RawMouse/build.sh  ->  Assets/SiegeSandbox/Plugins/macOS/libSiegeRawMouse.dylib

#import <Foundation/Foundation.h>
#import <GameController/GameController.h>
#include <stdatomic.h>

static _Atomic int sumX = 0, sumY = 0, reports = 0;
static float restX = 0.0f, restY = 0.0f;     // fractions carried over, so nothing is lost to rounding
static BOOL running = NO;
static id connectObserver = nil;

static void attachMouse(GCMouse *mouse) API_AVAILABLE(macos(11.0))
{
    mouse.mouseInput.mouseMovedHandler = ^(GCMouseInput *input, float deltaX, float deltaY) {
        restX += deltaX;
        restY += deltaY;
        int wholeX = (int)restX, wholeY = (int)restY;
        restX -= wholeX;
        restY -= wholeY;
        atomic_fetch_add(&sumX, wholeX);
        atomic_fetch_add(&sumY, wholeY);
        atomic_fetch_add(&reports, 1);
    };
}

// Start listening. Returns 1 when running, -1 where the system is too old to offer this.
int siege_rawmouse_start(void)
{
    if (@available(macOS 11.0, *))
    {
        if (running) return 1;
        running = YES;
        for (GCMouse *mouse in [GCMouse mice]) attachMouse(mouse);
        connectObserver = [[NSNotificationCenter defaultCenter] addObserverForName:GCMouseDidConnectNotification
            object:nil queue:[NSOperationQueue mainQueue] usingBlock:^(NSNotification *note) {
                if ([note.object isKindOfClass:[GCMouse class]]) attachMouse((GCMouse *)note.object);
            }];
        atomic_store(&sumX, 0);
        atomic_store(&sumY, 0);
        atomic_store(&reports, 0);
        return 1;
    }
    return -1;
}

void siege_rawmouse_stop(void)
{
    if (@available(macOS 11.0, *))
    {
        if (!running) return;
        running = NO;
        if (connectObserver != nil) [[NSNotificationCenter defaultCenter] removeObserver:connectObserver];
        connectObserver = nil;
        for (GCMouse *mouse in [GCMouse mice]) mouse.mouseInput.mouseMovedHandler = nil;
    }
}

// Movement since the last call, in the mouse's own counts (x right, y away from you).
// Returns how many reports that was.
int siege_rawmouse_read(int *x, int *y)
{
    *x = atomic_exchange(&sumX, 0);
    *y = atomic_exchange(&sumY, 0);
    return atomic_exchange(&reports, 0);
}

// How many mice the system is offering.
int siege_rawmouse_devices(void)
{
    if (@available(macOS 11.0, *)) return (int)[GCMouse mice].count;
    return 0;
}
