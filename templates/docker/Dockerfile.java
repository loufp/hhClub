FROM maven:3.8-openjdk-17 AS builder
WORKDIR /app
COPY pom.xml .
# ...existing code...
RUN mvn -B -DskipTests=true dependency:go-offline

COPY . .
RUN mvn -B -DskipTests=true package

FROM eclipse-temurin:17-jre
WORKDIR /app
COPY --from=builder /app/target/*.jar /app/app.jar
ENV JAVA_OPTS=""
ENTRYPOINT ["sh","-c","java $JAVA_OPTS -jar /app/app.jar"]

