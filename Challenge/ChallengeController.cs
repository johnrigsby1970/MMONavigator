namespace MMONavigator.Services;

public class ChallengeController {
    private DateTime lastTimestamp = DateTime.Now;
    private CoordinateData? lastPos;
    
    //Most games have a known movement speed (e.g., in EverQuest, a character at "run speed" might travel about 15–20 feet per second).
    //You should always set your MAX_RUN_SPEED at least 20-30% higher than the game's actual maximum speed to account for game latency
    private const double MAX_RUN_SPEED = 25; //fps
    
    public void CheckPosition(CoordinateData currentPos, CoordinateData targetPos) {
        if (IsNearTarget(currentPos, targetPos)) {
            // Trigger the "Anchor" timer or the "Knowledge Check" popup
        }
    }

    public static bool IsNearTarget(CoordinateData currentPos, CoordinateData targetPos, double thresholdFeet = 10,
        bool includeElevation = false) {
        // Calculate squared horizontal distance (X and Y)
        double dx = currentPos.X - targetPos.X;
        double dy = currentPos.Y - targetPos.Y;
        double horizontalDistSq = (dx * dx) + (dy * dy);

        // Early exit: if horizontal distance alone exceeds threshold, we don't care about Z
        if (horizontalDistSq > (thresholdFeet * thresholdFeet)) {
            return false;
        }

        // If elevation is required
        if (includeElevation) {
            // If either Z is missing, we must decide how to handle it.
            // Assuming if Z is null, we treat it as "at the same height" (0 difference)
            double dz = (currentPos.Z ?? targetPos.Z ?? 0) - (targetPos.Z ?? 0);
            double totalDistSq = horizontalDistSq + (dz * dz);

            return totalDistSq <= (thresholdFeet * thresholdFeet);
        }

        // Return true if we reached here (horizontal check passed, elevation ignored)
        return true;
    }

    public static double CalculateDistance(CoordinateData currentPos, CoordinateData targetPos,
        bool includeElevation = false) {
        // Calculate squared horizontal distance (X and Y)
        double dx = currentPos.X - targetPos.X;
        double dy = currentPos.Y - targetPos.Y;
        double horizontalDistSq = (dx * dx) + (dy * dy);

        // Early exit: if horizontal distance alone exceeds threshold, we don't care about Z
        if (!includeElevation) {
            return horizontalDistSq;
        }

        // If elevation is required
        // If either Z is missing, we must decide how to handle it.
        // Assuming if Z is null, we treat it as "at the same height" (0 difference)
        double dz = (currentPos.Z ?? targetPos.Z ?? 0) - (targetPos.Z ?? 0);
        double totalDistSq = horizontalDistSq + (dz * dz);

        return totalDistSq;
    }

    private void ProcessHeartbeat(CoordinateData currentPos, CoordinateData targetPos) {
        var currentTime = DateTime.Now;

        // 1. Check for Teleportation (Anti-Cheat)
        if (lastPos != null) {
            double distance = CalculateDistance(currentPos, lastPos.Value);
            double timeDelta = (currentTime - lastTimestamp).TotalSeconds;

            if (distance / timeDelta > MAX_RUN_SPEED) {
                MessageBox.Show("Excessive displacement: Teleportation");
                return;
            }
        }
       

        // // 2. Check for Static Stale Data
        // if (newData == lastPos) {
        //     // Increment "StallCounter". If > 3, pause Challenge.
        //     return;
        // }

        // 3. Normal Processing
        //UpdateBreadcrumbs(newData);
        CheckPosition(currentPos, targetPos);

        // 4. Reset Clipboard (The Wipe)
        ClearSystemClipboard();

        lastPos = currentPos;
        lastTimestamp = currentTime;
    }

    public void ClearSystemClipboard() {
        Clipboard.Clear();
    }
}