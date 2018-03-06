namespace Sitecore.Support.ContentSearch.Azure.Http
{
  using System;
  using System.Net;
  using System.Net.Http;
  using System.Threading;
  using Sitecore.ContentSearch.Azure.Http;
  using Sitecore.ContentSearch.Azure.PerformanceCounters;
  using Sitecore.Diagnostics;
  using Sitecore.Diagnostics.PerformanceCounters;

  [Obsolete("Should be dropped")]
  public class CloudSearchRetryPolicy : ICloudSearchRetryPolicy
  {
    public bool PerformanceCountersEnabled { get; set; }

    public ICloudSearchRetryStrategy RetryStrategy { get; set; }

    public ICloudSearchTransientDetectionStrategy DetectionStrategy { get; set; }

    public virtual HttpResponseMessage Execute(Func<HttpResponseMessage> action)
    {
      Assert.IsNotNull(this.DetectionStrategy, "Detection Stategy is not found");
      Assert.IsNotNull(this.RetryStrategy, "Retry Stategy is not found");

      var retryCount = 0;

      if (this.PerformanceCountersEnabled)
      {
        ContentSearchCounters.SearchRequests.Increment();
      }

      using (new PerformanceWatcher(this.PerformanceCountersEnabled ? ContentSearchCounters.SearchRequestsTime : null))
      {
        while (true)
        {
          TimeSpan interval;

          HttpResponseMessage result = null;

          try
          {
            using (new PerformanceWatcher(this.PerformanceCountersEnabled ? ContentSearchCounters.HttpRequestTime : null))
            {
              result = action();
            }

            if ((result.IsSuccessStatusCode && result.StatusCode != (HttpStatusCode)207) || !this.DetectionStrategy.IsTransient(result, null) || !this.RetryStrategy.ShouldRetry(retryCount, out interval))
            {
              if (this.PerformanceCountersEnabled)
              {
                ContentSearchCounters.HttpRequestsPerSearchRequest.AddMeasurement(retryCount + 1);
              }

              return result;
            }

            result.Dispose();
          }
          catch (Exception ex)
          {
            if (result != null)
            {
              result.Dispose();
            }

            if (!(this.DetectionStrategy.IsTransient(null, ex) && this.RetryStrategy.ShouldRetry(retryCount, out interval)))
            {
              if (this.PerformanceCountersEnabled)
              {
                ContentSearchCounters.HttpRequestsPerSearchRequest.AddMeasurement(retryCount + 1);
                ContentSearchCounters.SearchRequestsErrors.Increment();
              }

              throw;
            }

            Log.Debug(string.Format("Exception was thrown in Retryer: {0}", ex.Message), this);
          }

          Thread.Sleep(interval);

          retryCount++;
        }
      }
    }
  }

  public static class ContentSearchCounters
  {
    public static AmountPerSecondCounter SearchRequests
    {
      get;
      private set;
    }

    public static AmountPerSecondCounter SearchRequestsErrors
    {
      get;
      private set;
    }

    public static AverageCounter SearchRequestsTime
    {
      get;
      private set;
    }

    public static AverageCounter HttpRequestsPerSearchRequest
    {
      get;
      private set;
    }

    public static AverageCounter HttpRequestTime
    {
      get;
      private set;
    }

    static ContentSearchCounters()
    {
      ContentSearchCounters.SearchRequests = new AmountPerSecondCounter("Cloud Content Search | Search request / sec", "Sitecore.Cloud.ContentSearch");
      ContentSearchCounters.SearchRequestsErrors = new AmountPerSecondCounter("Cloud Content Search | Search request errors / sec", "Sitecore.Cloud.ContentSearch");
      ContentSearchCounters.SearchRequestsTime = new AverageCounter("Cloud Content Search | Average Search request time (ms)", "Sitecore.Cloud.ContentSearch");
      ContentSearchCounters.HttpRequestsPerSearchRequest = new AverageCounter("Cloud Content Search | Average Http request / Search request", "Sitecore.Cloud.ContentSearch");
      ContentSearchCounters.HttpRequestTime = new AverageCounter("Cloud Content Search | Average Http request time (ms)", "Sitecore.Cloud.ContentSearch");
    }
  }
}