#include <SPI.h>
#include "MAX31855.h"

#define DO   3
#define CS   4
#define CLK  5

// on MEGA use:
// pin 50 for DO (MISO - data from slave to master) 
// 52 for CLK (SCK - serial clock)

MAX31855 tc(CS);


void setup() 
{
  Serial.begin(115200);
  delay(500); // max chip to stabilize ??
}

void loop() 
{
  long t0, t1;
  t0 = millis();
  //tc.readCelsius();
  
  for (uint16_t k = 0; k < 100; k ++)
  {
    tc.readCelsius();
  }
  
  t1 = millis();
  Serial.println(t1 - t0);
  delay(1000);
  
  Serial.println(tc.readCelsius());
  
}
