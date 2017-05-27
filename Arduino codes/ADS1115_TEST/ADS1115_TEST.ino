#include "ADS1115.h"
#include <Wire.h>

ADS1115 *ads;
int data[4] = {};
long t = 0;

void setup() 
{
  Serial.begin(115200);
  ads = new ADS1115();
  ads->begin();
}

void loop() 
{
  ads->updateAll(data);

  if (millis() - t > 500)
  {
    for (int k = 0; k < 4; k++)
    {
      Serial.println(data[k]);
    }
    
    Serial.println();
    t = millis();
  }
  //delay(500);
}
