package shipping

import (
	"context"
	"fmt"
	"math/rand"

	"time"

	dapr "github.com/dapr/go-sdk/client"
	"github.com/sirupsen/logrus"
)

type Shipping struct {
	cfg *Configuration
	log *logrus.Logger
}

func NewShipping(cfg *Configuration, log *logrus.Logger) *Shipping {
	return &Shipping{
		cfg: cfg,
		log: log,
	}
}

const (
	environmentProduction = "Production"
)

type Configuration struct {
	PubSubName     string
	TopicName      string
	Environment    string
	ZipkinEndpoint string
}

func (c *Configuration) IsProduction() bool {
	return c.Environment == environmentProduction
}

type ShippingProcessed struct {
	UserId       string
	CustomerName string
	OrderId      string
}

type Order struct {
	Id           string
	UserId       string
	CustomerName string
	SubmittedAt  time.Time
	Positions    []OrderPosition
}

type OrderPosition struct {
	ProductId   string
	ProductName string
	Quantity    int
}

func (s *Shipping) ProcessOrder(o *Order) error {
	c, err := dapr.NewClient()
	if err != nil {
		return fmt.Errorf("Error while creating dapr client %s", err)
	}

	// invoke business logic ;-)
	s.log.Infof("Applying shipping business logic on message %s", o.Id)

	rand.Seed(time.Now().UnixNano())
	min := 1
	max := 5
	randomNumber := rand.Intn(max-min+1) + min

	s.log.Infof("Shipping business logic takes %d seconds", randomNumber)

	time.Sleep(time.Second * time.Duration(randomNumber))

	m := &ShippingProcessed{
		CustomerName: o.CustomerName,
		OrderId:      o.Id,
		UserId:       o.UserId,
	}
	s.log.Infof("Publishing event to %s %s", s.cfg.PubSubName, s.cfg.TopicName)
	if err = c.PublishEvent(context.Background(), s.cfg.PubSubName, s.cfg.TopicName, m); err != nil {
		return fmt.Errorf("Will fail because publishing order-processed event failed: %s", err)
	}
	return nil
}
