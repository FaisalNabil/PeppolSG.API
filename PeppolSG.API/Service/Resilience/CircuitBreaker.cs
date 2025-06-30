using System;
using System.Threading;

namespace PeppolSG.API.Service.Resilience
{
    /// <summary>
    /// Simple thread-safe circuit-breaker (closed → open → half-open).
    /// Failures counted in rolling window; after reset timeout the breaker enters half-open allowing a single test call.
    /// </summary>
    public class CircuitBreaker
    {
        public enum State { Closed, Open, HalfOpen }

        private readonly int failureThreshold;
        private readonly TimeSpan resetTimeout;

        private int failureCount;
        private State state = State.Closed;
        private DateTime openTimestamp;
        private readonly object sync = new object();

        public CircuitBreaker(int failureThreshold = 5, TimeSpan? resetTimeout = null)
        {
            this.failureThreshold = failureThreshold;
            this.resetTimeout = resetTimeout ?? TimeSpan.FromSeconds(60);
        }

        public State CurrentState
        {
            get
            {
                if (state == State.Open && DateTime.UtcNow - openTimestamp > resetTimeout)
                {
                    // transition to half-open after timeout
                    lock (sync)
                    {
                        if (state == State.Open && DateTime.UtcNow - openTimestamp > resetTimeout)
                        {
                            state = State.HalfOpen;
                        }
                    }
                }
                return state;
            }
        }

        /// <summary>
        /// Call before executing protected operation. Throws BrokenCircuitException if circuit is open.
        /// </summary>
        public void ThrowIfOpen()
        {
            if (CurrentState == State.Open)
                throw new BrokenCircuitException("Circuit is open");
        }

        public void OnSuccess()
        {
            // success in half-open closes circuit
            lock (sync)
            {
                failureCount = 0;
                state = State.Closed;
            }
        }

        public void OnFailure()
        {
            lock (sync)
            {
                failureCount++;
                if (failureCount >= failureThreshold)
                {
                    state = State.Open;
                    openTimestamp = DateTime.UtcNow;
                }
            }
        }
    }

    public class BrokenCircuitException : Exception
    {
        public BrokenCircuitException(string msg) : base(msg) { }
    }
} 