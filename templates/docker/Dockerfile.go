FROM golang:1.20-alpine AS builder
WORKDIR /app
ENV CGO_ENABLED=0
COPY go.mod go.sum ./
RUN go mod download
COPY . .
RUN go build -ldflags="-s -w" -o /app/bin/app ./...

FROM alpine:3.18
RUN addgroup -S app && adduser -S -G app app
COPY --from=builder /app/bin/app /usr/local/bin/app
USER app
ENTRYPOINT ["/usr/local/bin/app"]

