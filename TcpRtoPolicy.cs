using RRC.Abstractions;

namespace RRC.Student;

public sealed class TcpRtoPolicy : IRequestPolicy
{
    private const double Alpha = 0.125;   // RFC 6298: SRTT gain 1/8
    private const double Beta = 0.25;     // RFC 6298: RTTVAR gain 1/4
    private const int MaxAttempts = 3;

    private static readonly TimeSpan MinRto = TimeSpan.FromMilliseconds(30);
    private static readonly TimeSpan MaxRto = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan BaseRetryDelay = TimeSpan.FromMilliseconds(20);

    private double srtt = -1;   // -1 = not initialized yet
    private double rttvar = 0;

    public TimeSpan GetTimeout(RequestContext context, Random random)
    {
        double rto = srtt < 0 ? 1000 : srtt + 4 * rttvar;

        if (context.Attempt > 1)
            rto *= Math.Pow(2, context.Attempt - 1);

        rto = Math.Clamp(rto, MinRto.TotalMilliseconds, MaxRto.TotalMilliseconds);
        return TimeSpan.FromMilliseconds(rto);
    }

    public RetryDecision OnResult(RequestObservation observation, Random random)
    {
        if (observation.Outcome == RequestOutcome.Success)
        {
            double sample = observation.Elapsed.TotalMilliseconds;

            if (srtt < 0)
            {
                // First observation: RFC 6298 seeds SRTT=R, RTTVAR=R/2.
                srtt = sample;
                rttvar = sample / 2;
            }
            else
            {
                rttvar = (1 - Beta) * rttvar + Beta * Math.Abs(srtt - sample);
                srtt = (1 - Alpha) * srtt + Alpha * sample;
            }

            return RetryDecision.NoRetry();
        }


        if (observation.Attempt < MaxAttempts)
        {
            double delay = BaseRetryDelay.TotalMilliseconds * observation.Attempt;
            return RetryDecision.RetryAfter(TimeSpan.FromMilliseconds(delay));
        }

        return RetryDecision.NoRetry();
    }
}
