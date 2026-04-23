FROM alpine:3.19

RUN apk add --no-cache bash curl openssl jq

COPY run.sh /run.sh
COPY enroll.sh /enroll.sh
COPY renew.sh /renew.sh

RUN chmod +x /run.sh /enroll.sh /renew.sh

CMD [ "/run.sh" ]