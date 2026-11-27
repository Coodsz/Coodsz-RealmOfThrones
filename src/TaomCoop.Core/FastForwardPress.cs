namespace TaomCoop.Core;

public static class FastForwardPress
{
    public static bool LeaveTaomTickAlone(bool coopSession, bool hostAllowsFastForward, bool thisProcessIsServer)
    {
        if (!coopSession)
            return true;
        return hostAllowsFastForward && thisProcessIsServer;
    }

    public static bool SendButtonToServer(bool coopSession, bool thisProcessIsServer, bool buttonPressed)
    {
        return coopSession && !thisProcessIsServer && buttonPressed;
    }
}
