#!/bin/bash

echo ">> Generating private key"
openssl genrsa -out $CERT_DIR/device.key 2048

chmod 600 $CERT_DIR/device.key

echo ">> Generating CSR"
openssl req -new -key $CERT_DIR/device.key \
-out $CERT_DIR/device.csr \
-subj "/CN=$AFFAIRE" \
-addext "subjectAltName=DNS:$AFFAIRE"

CSR=$(awk '{printf "%s\\n", $0}' $CERT_DIR/device.csr)
echo "La CSR : est $CSR"
echo ">> Calling enrollment API"

RESPONSE=$(curl -s -X POST "$API_URL/enroll" \
  -H "Content-Type: application/json" \
  -d "{
    \"affaire\": \"$AFFAIRE\",
    \"token\": \"$TOKEN\",
    \"csr\": \"$CSR\"
  }")

echo "$RESPONSE" | jq -r '.cert' > $CERT_DIR/device.crt
echo "$RESPONSE" | jq -r '.ca' > $CERT_DIR/ca.crt

chmod 644 $CERT_DIR/device.crt
chmod 644 $CERT_DIR/ca.crt

echo ">> Enrollment done"