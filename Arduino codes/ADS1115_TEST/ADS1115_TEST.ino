#include "ADS1115.h"
#include <Wire.h>

// ADC object: possible sample rates: 860 475 250 128
ADS1115 ads = ADS1115(475);
int data[4] = {};
long t = 0;

void setup() 
{
  Serial.begin(115200);
//  ads = new ADS1115(450);
  ads.begin();
}

void loop() 
{
  ads.updateAll(data);

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
