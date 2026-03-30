import { initializeFaro, getWebInstrumentations } from '@grafana/faro-web-sdk'
import { TracingInstrumentation } from '@grafana/faro-web-tracing'

const apiUrl = import.meta.env.VITE_API_URL ?? 'http://localhost:8080'
const faroUrl = import.meta.env.VITE_FARO_URL ?? 'http://localhost:12347/collect'

initializeFaro({
  url: faroUrl,
  app: {
    name: 'spa',
    version: '1.0.0',
    environment: 'playground',
  },
  instrumentations: [
    ...getWebInstrumentations({
      captureConsole: true,
    }),
    new TracingInstrumentation({
      instrumentationOptions: {
        // injects traceparent header on fetch to dotnet-app
        // connects browser spans to backend spans in a single trace
        propagateTraceHeaderCorsUrls: [new RegExp(apiUrl)],
        fetchInstrumentationOptions: {
          applyCustomAttributesOnSpan(span, request) {
            const url = request instanceof Request ? request.url : undefined
            const method = request instanceof Request ? (request.method ?? 'GET') : 'GET'
            if (!url) return
            try {
              const { pathname } = new URL(url)
              span.updateName(`${method} ${pathname}`)
            } catch {
              // unparseable URL — leave default name
            }
          },
        },
      },
    }),
  ],
})
